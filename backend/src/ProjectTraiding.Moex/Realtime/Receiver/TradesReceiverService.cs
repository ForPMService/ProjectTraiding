using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Realtime.Series;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём ленты сделок по всем инструментам. Курсор TRADENO и покрытие
    /// ведутся отдельно по каждому инструменту; сбой одного инструмента не отменяет оборот.
    /// </summary>
    public sealed class TradesReceiverService : RealtimeReceiverServiceBase<TradesInstrumentState>
    {
        protected override string DataKind => "trades";

        protected override TimeSpan StalePollThreshold => _stalePollThreshold;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TradesReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _stalePollThreshold;
        private bool _initialized;

        public TradesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<TradesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout,
            TimeSpan stalePollThreshold)
            : base(pollInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _instrumentFetchTimeout = instrumentFetchTimeout;
            _stalePollThreshold = stalePollThreshold;
        }

        protected override void LogStarted(TimeSpan pollInterval)
            => MoexRealtimeReceiverLogMessages.TradesStarted(_logger, pollInterval);

        protected override void LogTurnFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.TradesTurnFailed(_logger, exception);

        protected override void LogStopped()
            => MoexRealtimeReceiverLogMessages.TradesStopped(_logger);

        protected override async Task RunTurnAsync(CancellationToken ct)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

                MoexReceiverInstrumentReader instrumentReader =
                    scope.ServiceProvider.GetRequiredService<MoexReceiverInstrumentReader>();
                StreamCursorWriter cursorWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCursorWriter>();

                MoexRealtimeRestClient client =
                    scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();

                IReadOnlyList<ReceiverInstrument> instruments =
                    await instrumentReader.GetEnabledForDataKindAsync(DataKind, ct);
                if (!_initialized)
                {
                    if (instruments.Count == 0)
                        MoexRealtimeReceiverLogMessages.TradesSubscriptionsEmpty(_logger);

                    await PrepareInitialInstrumentsAsync(
                        scope.ServiceProvider, instruments, ct);
                    _initialized = true;
                }
                else
                {
                    await ReconcileStatesAsync(
                        scope.ServiceProvider, instruments, ct);
                }

                ClickHouseInsertExecutor executor =
                    scope.ServiceProvider.GetRequiredService<ClickHouseInsertExecutor>();
                foreach (KeyValuePair<string, TradesInstrumentState> pair in States)
                {
                    try
                    {
                        await PollAsync(
                            pair.Key,
                            pair.Value,
                            client,
                            executor,
                            cursorWriter,
                            ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        MoexRealtimeReceiverLogMessages.TradesInstrumentPollFailed(
                            _logger, ex, pair.Key, pair.Value.Market);
                    }
                }
            }
            finally
            {
                // Агрегат публикуется после любого исхода оборота. Иначе при отказе
                // чтения подписок или согласования состояний числа активных и отставших
                // инструментов застынут на прежних значениях, а разрезы снятых подписок
                // не удалятся — до следующего полностью успешного оборота.
                try
                {
                    PublishTelemetrySnapshots();
                }
                catch
                {
                    // Сбой публикации телеметрии не меняет исход оборота.
                }
            }
        }

        protected override async Task OpenStateAsync(
            IServiceProvider services,
            ReceiverInstrument instrument,
            string boardId,
            CancellationToken ct)
        {
            StreamCursorWriter cursorWriter =
                services.GetRequiredService<StreamCursorWriter>();
            MoexRealtimeRestClient client =
                services.GetRequiredService<MoexRealtimeRestClient>();
            StreamCursorState? cursor = await cursorWriter.TryGetAsync(
                instrument.Secid,
                instrument.Market,
                boardId,
                DataKind,
                null,
                ct);

            long? initialAfterTradeNo = cursor?.LastTradeNo;

            // Холодный старт (курсора в PostgreSQL нет): встаём на текущий хвост одним обратным
            // запросом (reversed=1) и фиксируем курсор, чтобы не переигрывать весь торговый день.
            // Пропущенную историю догружает исторический загрузчик — это его зона ответственности.
            if (initialAfterTradeNo is null)
            {
                (long TradeNo, DateTime SourceTime)? tail =
                    await TrySeedTailAsync(instrument, client, ct);
                if (tail is not null)
                {
                    await UpdateCursorAsync(
                        cursorWriter,
                        instrument.Secid,
                        instrument.Market,
                        boardId,
                        DataKind,
                        null,
                        tail.Value.SourceTime,
                        tail.Value.TradeNo,
                        ct);
                    initialAfterTradeNo = tail.Value.TradeNo;
                    MoexRealtimeReceiverLogMessages.TradesInstrumentSeeded(
                        _logger, instrument.Secid, instrument.Market, tail.Value.TradeNo);
                }
            }


            States.Add(
                instrument.Secid,
                new TradesInstrumentState(
                    initialAfterTradeNo,
                    instrument.Market,
                    boardId));
            MoexRealtimeReceiverLogMessages.TradesInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market);
        }

        private async Task PollAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            ClickHouseInsertExecutor executor,
            StreamCursorWriter cursorWriter,
            CancellationToken commitCt)
        {
            if (state.Market == MoexMarkets.Stock)
            {
                await PollStockAsync(secid, state, client, executor, cursorWriter, commitCt);
                return;
            }
            if (state.Market == MoexMarkets.Futures)
            {
                await PollFuturesAsync(secid, state, client, executor, cursorWriter, commitCt);
                return;
            }
            throw new InvalidOperationException(
                $"Неизвестный рынок инструмента приёмника: '{state.Market}'.");
        }

        private async Task PollStockAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            ClickHouseInsertExecutor executor,
            StreamCursorWriter cursorWriter,
            CancellationToken commitCt)
        {
            MoexOperationTags operationTags = new(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeTradesPoll,
                MoexDataKinds.Trades,
                state.Market,
                MoexFlows.Realtime);
            StorageInsertContext insertContext = new(
                operationTags.DataKind, operationTags.Market, MoexFlows.Realtime);
            ProtobufBatchWriter<RealtimeStockTradeRow> writer = new(
                executor,
                MoexSeriesRegistry.RealtimeStockTradesTable,
                RealtimeStockTradeRow.InsertColumns,
                insertContext);
            using Activity? pollActivity =
                MoexTelemetry.ActivitySource.StartActivity("moex.realtime.instrument.poll");
            pollActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
            pollActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
            pollActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);

            long fetchStart = Stopwatch.GetTimestamp();
            int acceptedRowsCount = 0;
            int rowFailuresCount = 0;
            DateTime lastTime = default;
            long? lastTradeNo = null;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                using Activity? fetchActivity =
                    MoexTelemetry.ActivitySource.StartActivity("moex.realtime.fetch");
                fetchActivity?.SetTag(MoexTelemetryAttributes.Source, operationTags.Source);
                fetchActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
                fetchActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
                fetchActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);
                try
                {
                    using (RentedBuffer body = await client.GetStockTradesAsync(
                               secid, state.AfterTradeNo, cancellationToken: fetchCts.Token))
                    {
                        MoexRealtimeParser.ParseStockTrades(
                            body.Span, secid, writer, state.AfterTradeNo,
                            appendRows: true, logger: _logger,
                            out _, out acceptedRowsCount, out rowFailuresCount,
                            out lastTime, out lastTradeNo, out _, out _, out _);
                    }
                    RecordTradeRowsReceived(operationTags, acceptedRowsCount);
                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error);
                    pollActivity?.SetStatus(ActivityStatusCode.Error);
                    MoexRealtimeReceiverLogMessages.TradesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    MoexMetrics.RecordOperationTimeout(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                    return;
                }
                catch (Exception ex)
                {
                    if (ex is MoexSchemaMismatchException)
                    {
                        MoexLogMessages.ParseFailed(
                            _logger, ex, BuildTradeEndpoint(state.Market, secid),
                            MoexErrorTypes.SchemaMismatch, ex.Message);
                    }
                    fetchActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    MoexMetrics.RecordOperationError(
                        in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                    throw;
                }
            }

            try
            {
                if (writer.RowCount > 0)
                {
                    int expectedRows = writer.RowCount;
                    long insertedRows = await writer.FlushAsync(commitCt);
                    if (insertedRows != expectedRows)
                    {
                        throw new InvalidOperationException(
                            $"ClickHouse подтвердил запись {insertedRows} из {expectedRows} сделок.");
                    }
                    state.RowsTotal += insertedRows;
                    state.LastConfirmedMarketTime = MoexTime.ToDateTimeOffset(lastTime);

                    if (rowFailuresCount == 0)
                    {
                        long confirmedTradeNo = lastTradeNo!.Value;
                        await UpdateCursorAsync(
                            cursorWriter, secid, state.Market, state.BoardId,
                            DataKind, null, lastTime, confirmedTradeNo, commitCt);
                        state.AfterTradeNo = confirmedTradeNo;
                    }
                }
                if (rowFailuresCount > 0)
                    throw new InvalidOperationException("Страница сделок обработана частично.");

                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Success);
                state.LastSuccessfulPollTime = DateTimeOffset.UtcNow;
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
                throw;
            }
            catch (Exception ex)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                throw;
            }
        }

        private async Task PollFuturesAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            ClickHouseInsertExecutor executor,
            StreamCursorWriter cursorWriter,
            CancellationToken commitCt)
        {
            MoexOperationTags operationTags = new(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeTradesPoll,
                MoexDataKinds.Trades,
                state.Market,
                MoexFlows.Realtime);
            StorageInsertContext insertContext = new(
                operationTags.DataKind, operationTags.Market, MoexFlows.Realtime);
            ProtobufBatchWriter<RealtimeFuturesTradeRow> writer = new(
                executor,
                MoexSeriesRegistry.RealtimeFuturesTradesTable,
                RealtimeFuturesTradeRow.InsertColumns,
                insertContext);
            using Activity? pollActivity =
                MoexTelemetry.ActivitySource.StartActivity("moex.realtime.instrument.poll");
            pollActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
            pollActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
            pollActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);

            long fetchStart = Stopwatch.GetTimestamp();
            int acceptedRowsCount = 0;
            int rowFailuresCount = 0;
            DateTime lastTime = default;
            long? lastTradeNo = null;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                using Activity? fetchActivity =
                    MoexTelemetry.ActivitySource.StartActivity("moex.realtime.fetch");
                fetchActivity?.SetTag(MoexTelemetryAttributes.Source, operationTags.Source);
                fetchActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
                fetchActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
                fetchActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);
                try
                {
                    using (RentedBuffer body = await client.GetFuturesTradesAsync(
                               secid, state.AfterTradeNo, cancellationToken: fetchCts.Token))
                    {
                        MoexRealtimeParser.ParseFuturesTrades(
                            body.Span, secid, writer, state.AfterTradeNo,
                            appendRows: true, logger: _logger,
                            out _, out acceptedRowsCount, out rowFailuresCount,
                            out lastTime, out lastTradeNo, out _, out _, out _);
                    }
                    RecordTradeRowsReceived(operationTags, acceptedRowsCount);
                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error);
                    pollActivity?.SetStatus(ActivityStatusCode.Error);
                    MoexRealtimeReceiverLogMessages.TradesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    MoexMetrics.RecordOperationTimeout(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                    return;
                }
                catch (Exception ex)
                {
                    if (ex is MoexSchemaMismatchException)
                    {
                        MoexLogMessages.ParseFailed(
                            _logger, ex, BuildTradeEndpoint(state.Market, secid),
                            MoexErrorTypes.SchemaMismatch, ex.Message);
                    }
                    fetchActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    MoexMetrics.RecordOperationError(
                        in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                    throw;
                }
            }

            try
            {
                if (writer.RowCount > 0)
                {
                    int expectedRows = writer.RowCount;
                    long insertedRows = await writer.FlushAsync(commitCt);
                    if (insertedRows != expectedRows)
                    {
                        throw new InvalidOperationException(
                            $"ClickHouse подтвердил запись {insertedRows} из {expectedRows} сделок.");
                    }
                    state.RowsTotal += insertedRows;
                    state.LastConfirmedMarketTime = MoexTime.ToDateTimeOffset(lastTime);

                    if (rowFailuresCount == 0)
                    {
                        long confirmedTradeNo = lastTradeNo!.Value;
                        await UpdateCursorAsync(
                            cursorWriter, secid, state.Market, state.BoardId,
                            DataKind, null, lastTime, confirmedTradeNo, commitCt);
                        state.AfterTradeNo = confirmedTradeNo;
                    }
                }
                if (rowFailuresCount > 0)
                    throw new InvalidOperationException("Страница сделок обработана частично.");

                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Success);
                state.LastSuccessfulPollTime = DateTimeOffset.UtcNow;
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
                throw;
            }
            catch (Exception ex)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                throw;
            }
        }
        private static void RecordTradeRowsReceived(MoexOperationTags operationTags, int rowCount)
        {
            TagList rowsTags = new TagList
            {
                { MoexTelemetryAttributes.Source, operationTags.Source },
                { MoexTelemetryAttributes.DataKind, operationTags.DataKind },
                { MoexTelemetryAttributes.Market, operationTags.Market },
                { MoexTelemetryAttributes.Flow, operationTags.Flow },
            };
            MoexMetrics.RowsReceived.Add(rowCount, rowsTags);
        }

        private static string BuildTradeEndpoint(string market, string secid)
        {
            if (market == MoexMarkets.Stock)
                return $"/engines/stock/markets/shares/boards/TQBR/securities/{secid}/trades.json";
            return $"/engines/futures/markets/forts/boards/RFUD/securities/{secid}/trades.json";
        }

        private static async Task UpdateCursorAsync(
            StreamCursorWriter cursorWriter,
            string secid,
            string market,
            string boardId,
            string dataKind,
            int? candleInterval,
            DateTime lastSourceTime,
            long lastTradeNo,
            CancellationToken ct)
        {
            using Activity? cursorActivity =
                MoexTelemetry.ActivitySource.StartActivity("storage.cursor.update");
            cursorActivity?.SetTag(MoexTelemetryAttributes.DataKind, dataKind);
            cursorActivity?.SetTag(MoexTelemetryAttributes.Market, market);
            cursorActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);

            try
            {
                await cursorWriter.UpsertAsync(
                    secid,
                    market,
                    boardId,
                    dataKind,
                    candleInterval,
                    lastSourceTime,
                    lastTradeNo,
                    ct);
                cursorActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (OperationCanceledException)
            {
                cursorActivity?.SetStatus(ActivityStatusCode.Ok);
                throw;
            }
            catch (Exception ex)
            {
                cursorActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        private async Task<(long TradeNo, DateTime SourceTime)?> TrySeedTailAsync(
            ReceiverInstrument instrument,
            MoexRealtimeRestClient client,
            CancellationToken ct)
        {
            Dictionary<string, string> reversedParams =
                new Dictionary<string, string> { ["reversed"] = "1" };

            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeTradesPoll,
                MoexDataKinds.Trades,
                instrument.Market,
                MoexFlows.Realtime);

            using Activity? fetchActivity =
                MoexTelemetry.ActivitySource.StartActivity("moex.realtime.fetch");
            fetchActivity?.SetTag(MoexTelemetryAttributes.Source, operationTags.Source);
            fetchActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
            fetchActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
            fetchActivity?.SetTag(MoexTelemetryAttributes.Secid, instrument.Secid);

            long fetchStart = Stopwatch.GetTimestamp();

            try
            {
                (long TradeNo, DateTime SourceTime)? tail;
                if (instrument.Market == MoexMarkets.Stock)
                {
                    int acceptedRowsCount;
                    int rowFailuresCount;
                    long? maxTradeNo;
                    DateTime? maxTradeNoTime;
                    Exception? maxTradeNoTimeFailure;
                    using (RentedBuffer body = await client.GetStockTradesAsync(
                               instrument.Secid, null, reversedParams, ct))
                    {
                        MoexRealtimeParser.ParseStockTrades(
                            body.Span, instrument.Secid, writer: null, afterTradeNo: null,
                            appendRows: false, logger: _logger,
                            out _, out acceptedRowsCount, out rowFailuresCount,
                            out _, out _, out maxTradeNo,
                            out maxTradeNoTime, out maxTradeNoTimeFailure);
                    }
                    RecordTradeRowsReceived(operationTags, acceptedRowsCount);
                    if (rowFailuresCount > 0)
                        throw new InvalidOperationException("Хвост сделок обработан частично.");
                    tail = PickTail(
                        maxTradeNo, maxTradeNoTime, maxTradeNoTimeFailure);
                }
                else if (instrument.Market == MoexMarkets.Futures)
                {
                    int acceptedRowsCount;
                    int rowFailuresCount;
                    long? maxTradeNo;
                    DateTime? maxTradeNoTime;
                    Exception? maxTradeNoTimeFailure;
                    using (RentedBuffer body = await client.GetFuturesTradesAsync(
                               instrument.Secid, null, reversedParams, ct))
                    {
                        MoexRealtimeParser.ParseFuturesTrades(
                            body.Span, instrument.Secid, writer: null, afterTradeNo: null,
                            appendRows: false, logger: _logger,
                            out _, out acceptedRowsCount, out rowFailuresCount,
                            out _, out _, out maxTradeNo,
                            out maxTradeNoTime, out maxTradeNoTimeFailure);
                    }
                    RecordTradeRowsReceived(operationTags, acceptedRowsCount);
                    if (rowFailuresCount > 0)
                        throw new InvalidOperationException("Хвост сделок обработан частично.");
                    tail = PickTail(
                        maxTradeNo, maxTradeNoTime, maxTradeNoTimeFailure);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Неизвестный рынок инструмента приёмника: '{instrument.Market}'.");
                }

                MoexMetrics.RecordOperationSuccess(
                    in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                return tail;
            }
            catch (OperationCanceledException)
            {
                fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                MoexMetrics.RecordOperationCancelled(
                    in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                throw;
            }
            catch (Exception ex)
            {
                if (ex is MoexSchemaMismatchException)
                {
                    MoexLogMessages.ParseFailed(
                        _logger, ex, BuildTradeEndpoint(instrument.Market, instrument.Secid),
                        MoexErrorTypes.SchemaMismatch, ex.Message);
                }
                fetchActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                MoexMetrics.RecordOperationError(
                    in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                throw;
            }
        }

        private static (long TradeNo, DateTime SourceTime)? PickTail(
            long? maxTradeNo,
            DateTime? maxTradeNoTime,
            Exception? maxTradeNoTimeFailure)
        {
            // Строки с пустым номером сделки холодный старт пропускает молча — их отсеял
            // сам разборщик, отбирая наибольший номер. Общий признак отвергнутой строки
            // на этом пути не смотрят вовсе: сегодня отбор хвоста страницу не отвергает.
            if (maxTradeNo is null)
                return null;

            // Но у выбранной строки момент обязан быть: сегодня отбор хвоста вызывает сборку
            // момента именно на ней, и она отказывает. Причин отказа две и они различны —
            // пустые дата или время дают один отказ, непустые неверного формата другой, —
            // поэтому поднимается сохранённая причина, а не одна на оба случая.
            if (maxTradeNoTime is null)
            {
                throw maxTradeNoTimeFailure
                    ?? new InvalidOperationException(
                        "Строка статистики отвергнута: пустые дата или время торгов.");
            }

            return (maxTradeNo.Value, maxTradeNoTime.Value);
        }

        protected override void LogInstrumentPreparationFailed(Exception exception, string secid)
            => MoexRealtimeReceiverLogMessages.TradesInstrumentPreparationFailed(
                _logger, exception, secid);

        protected override void LogInstrumentStopped(string secid, long rowsTotal)
            => MoexRealtimeReceiverLogMessages.TradesInstrumentStopped(
                _logger, secid, rowsTotal);

    }

    public sealed class TradesInstrumentState : ReceiverInstrumentState
    {
        public TradesInstrumentState(
            long? afterTradeNo,
            string market,
            string boardId)
            : base(market, boardId)
        {
            AfterTradeNo = afterTradeNo;
        }

        /// <summary>
        /// Номер сделки, после которого выполняется следующий запрос. Оперативная копия
        /// внутри процесса: двигается сразу за постоянным курсором и только после
        /// успешной записи страницы, чтобы не разойтись с ним при сбое.
        /// </summary>
        public long? AfterTradeNo { get; set; }
    }
}
