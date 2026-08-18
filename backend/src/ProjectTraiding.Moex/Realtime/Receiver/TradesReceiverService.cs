using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Realtime.Series;
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

        protected override int? StorageCandleInterval => null;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TradesReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _heartbeatMinInterval;
        private readonly TimeSpan _stalePollThreshold;
        private bool _initialized;

        public TradesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<TradesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout,
            TimeSpan heartbeatMinInterval,
            TimeSpan stalePollThreshold)
            : base(pollInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _instrumentFetchTimeout = instrumentFetchTimeout;
            _heartbeatMinInterval = heartbeatMinInterval;
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
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                MoexRealtimeRestClient client =
                    scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();

                IReadOnlyList<ReceiverInstrument> instruments =
                    await instrumentReader.GetEnabledForDataKindAsync(DataKind, ct);
                if (!_initialized)
                {
                    if (instruments.Count == 0)
                        MoexRealtimeReceiverLogMessages.TradesSubscriptionsEmpty(_logger);

                    await PrepareInitialInstrumentsAsync(
                        scope.ServiceProvider, instruments, coverageWriter, ct);
                    _initialized = true;
                }
                else
                {
                    await ReconcileStatesAsync(
                        scope.ServiceProvider, instruments, coverageWriter, ct);
                }

                RealtimeSpecRowWriter writer =
                    scope.ServiceProvider.GetRequiredService<RealtimeSpecRowWriter>();
                foreach (KeyValuePair<string, TradesInstrumentState> pair in States)
                {
                    if (pair.Value.IsStopping)
                        continue;

                    try
                    {
                        await PollAsync(
                            pair.Key,
                            pair.Value,
                            client,
                            writer,
                            cursorWriter,
                            coverageWriter,
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
            StreamCoverageWriter coverageWriter =
                services.GetRequiredService<StreamCoverageWriter>();
            MoexRealtimeRestClient client =
                services.GetRequiredService<MoexRealtimeRestClient>();
            MoexDataGenerationReader generationReader =
                services.GetRequiredService<MoexDataGenerationReader>();

            // Чтение идёт до курсора, холодного старта и открытия сеанса: отказ здесь
            // не обесценивает уже проделанную работу и не оставляет открытой строки покрытия.
            string dataGeneration = await generationReader.GetAsync(instrument.Secid, ct);

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

            long sessionId = await coverageWriter.OpenSessionAsync(
                instrument.Secid, instrument.Market, boardId, DataKind, null, ct);
            long heartbeatTimestamp = Stopwatch.GetTimestamp();

            States.Add(
                instrument.Secid,
                new TradesInstrumentState(
                    initialAfterTradeNo,
                    sessionId,
                    instrument.Market,
                    boardId,
                    heartbeatTimestamp)
                {
                    DataGeneration = dataGeneration,
                });
            MoexRealtimeReceiverLogMessages.TradesInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private async Task PollAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeSpecRowWriter writer,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            MoexRealtimeSpec spec = MoexRealtimeRegistry.TradesFor(state.Market);

            // Получение одной страницы — под собственным бюджетом, живущим строго вокруг вызова
            // клиента. По истечении RealtimeInstrumentFetchTimeout получение отменяется, инструмент
            // пропускается до следующего оборота. Бюджет уничтожается ЗДЕСЬ, до фиксации: иначе его
            // таймер, сработав во время медленной записи, мог бы ошибочно попасть на исключение
            // фазы фиксации и выдать сбой хранилища за тайм-аут получения.
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeTradesPoll,
                MoexDataKinds.Trades,
                state.Market,
                MoexFlows.Realtime);

            StorageInsertContext insertContext = new StorageInsertContext(
                operationTags.DataKind,
                operationTags.Market,
                MoexFlows.Realtime);

            RealtimeParsedPage page;
            using (Activity? pollActivity =
                   MoexTelemetry.ActivitySource.StartActivity("moex.realtime.instrument.poll"))
            {
            pollActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
            pollActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
            pollActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);

            long fetchStart = Stopwatch.GetTimestamp();

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
                    page = await client.GetTradesAsync(
                        state.Market,
                        secid,
                        state.AfterTradeNo,
                        cancellationToken: fetchCts.Token);
                    MoexMetrics.RowsReceived.Add(
                        page.Rows.Count,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, operationTags.Source),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, operationTags.DataKind),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, operationTags.Market),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Flow, operationTags.Flow));
                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                    pollActivity?.SetStatus(ActivityStatusCode.Ok);
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
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
                if (page.Rows.Count > 0)
                {
                    // Отказ построчного стража поднимается здесь, а не в разборе: к этому
                    // моменту успех получения и число полученных строк уже учтены, и
                    // наблюдаемая картина остаётся прежней — успешное получение,
                    // засчитанные строки, неуспешный оборот.
                    //
                    // Проверка стоит внутри ветви непустой пачки намеренно: сегодня стражи
                    // живут в карте столбцов, а карта на пустой странице не вызывается вовсе,
                    // и такой оборот заканчивается успешно.
                    // Бросается сохранённое исключение, а не новое поверх его текста:
                    // тип отказа виден в записи журнала и подменяться не должен.
                    if (page.GuardFailure is not null) throw page.GuardFailure;

                    // Фиксация страницы — под хостовым commitCt. Обрыв бюджетом между записью и курсором
                    // недопустим: следующий оборот со старым курсором получит расширенную пачку с другим
                    // токеном, insert-дедупликация её примет, и до фонового слияния будут видны лишние
                    // физические версии. ReplacingMergeTree схлопнет их по ключу при слиянии; чтение до
                    // этого требует FINAL. Порядок: durable-запись, затем сразу in-memory состояние.
                    await writer.WriteAsync(
                        spec,
                        secid,
                        state.DataGeneration,
                        page.Rows,
                        page.FirstTime,
                        page.LastTime,
                        insertContext,
                        commitCt);

                    long lastTradeNo = page.LastTradeNo!.Value;
                    await UpdateCursorAsync(
                        cursorWriter,
                        secid,
                        state.Market,
                        state.BoardId,
                        DataKind,
                        null,
                        page.LastTime,
                        lastTradeNo,
                        commitCt);

                    // In-memory состояние двигается сразу за durable-курсором — чтобы не разойтись с ним,
                    // если сердцебиение упадёт.
                    state.RowsTotal += page.Rows.Count;
                    state.AfterTradeNo = lastTradeNo;
                    state.LastConfirmedMarketTime = MoexTime.ToDateTimeOffset(page.LastTime);
                }

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
            catch (Exception)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Error);
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                throw;
            }
            }

            await ReceiverSessionHeartbeat.WriteIfDueAsync(
                state, _heartbeatMinInterval, coverageWriter, commitCt);
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

        private static async Task<(long TradeNo, DateTime SourceTime)?> TrySeedTailAsync(
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
                RealtimeParsedPage page = await client.GetTradesAsync(
                    instrument.Market, instrument.Secid, null, reversedParams, ct);
                MoexMetrics.RowsReceived.Add(
                    page.Rows.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, operationTags.Source),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, operationTags.DataKind),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, operationTags.Market),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Flow, operationTags.Flow));

                // Здесь и только здесь — на месте нынешнего отбора хвоста, между счётчиком
                // строк и записью успеха операции. Иначе меняется наблюдаемая картина отказа.
                (long TradeNo, DateTime SourceTime)? tail = PickTail(page);

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
                fetchActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                MoexMetrics.RecordOperationError(
                    in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                throw;
            }
        }

        private static (long TradeNo, DateTime SourceTime)? PickTail(
            RealtimeParsedPage page)
        {
            // Строки с пустым номером сделки холодный старт пропускает молча — их отсеял
            // сам разборщик, отбирая наибольший номер. Общий признак отвергнутой строки
            // на этом пути не смотрят вовсе: сегодня отбор хвоста страницу не отвергает.
            if (page.MaxTradeNo is null)
                return null;

            // Но у выбранной строки момент обязан быть: сегодня отбор хвоста вызывает сборку
            // момента именно на ней, и она отказывает. Причин отказа две и они различны —
            // пустые дата или время дают один отказ, непустые неверного формата другой, —
            // поэтому поднимается сохранённая причина, а не одна на оба случая.
            if (page.MaxTradeNoTime is null)
            {
                throw page.MaxTradeNoTimeFailure
                    ?? new InvalidOperationException(
                        "Строка статистики отвергнута: пустые дата или время торгов.");
            }

            return (page.MaxTradeNo.Value, page.MaxTradeNoTime.Value);
        }

        protected override IServiceScopeFactory ScopeFactory => _scopeFactory;

        protected override void LogSessionCloseFailed(Exception exception, string secid, long sessionId)
            => MoexRealtimeReceiverLogMessages.TradesSessionCloseFailed(
                _logger, exception, secid, sessionId);

        protected override void LogShutdownFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.TradesShutdownFailed(_logger, exception);

        protected override void LogInstrumentPreparationFailed(Exception exception, string secid)
            => MoexRealtimeReceiverLogMessages.TradesInstrumentPreparationFailed(
                _logger, exception, secid);

        protected override void LogInstrumentStopping(string secid, long sessionId)
            => MoexRealtimeReceiverLogMessages.TradesInstrumentStopping(_logger, secid, sessionId);

        protected override void LogInstrumentStopped(string secid, long sessionId, long rowsTotal)
            => MoexRealtimeReceiverLogMessages.TradesInstrumentStopped(
                _logger, secid, sessionId, rowsTotal);

    }

    public sealed class TradesInstrumentState : ReceiverInstrumentSessionState
    {
        public TradesInstrumentState(
            long? afterTradeNo,
            long sessionId,
            string market,
            string boardId,
            long lastHeartbeatTimestamp)
            : base(sessionId, market, boardId, lastHeartbeatTimestamp)
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
