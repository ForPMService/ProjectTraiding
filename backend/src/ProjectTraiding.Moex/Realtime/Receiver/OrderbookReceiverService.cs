using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём полных снимков стакана по всем инструментам. Стакан ведёт только
    /// покрытие: курсора у полного снимка нет и в moex_stream_cursors он не записывается.
    /// </summary>
    public sealed class OrderbookReceiverService : RealtimeReceiverServiceBase
    {
        private const string StockMarket = "stock";
        private const string FuturesMarket = "futures";
        private const string StockBoardId = "TQBR";
        private const string FuturesBoardId = "RFUD";
        private const string DataKind = "orderbook";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderbookReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _heartbeatMinInterval;
        private readonly TimeSpan _stalePollThreshold;
        private readonly Dictionary<string, ReceiverInstrumentSessionState> _states =
            new Dictionary<string, ReceiverInstrumentSessionState>();
        private bool _initialized;

        public OrderbookReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<OrderbookReceiverService> logger,
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
            => MoexRealtimeReceiverLogMessages.OrderbookStarted(_logger, pollInterval);

        protected override void LogTurnFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.OrderbookTurnFailed(_logger, exception);

        protected override void LogStopped()
            => MoexRealtimeReceiverLogMessages.OrderbookStopped(_logger);

        protected override async Task RunTurnAsync(CancellationToken ct)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

                MoexReceiverInstrumentReader instrumentReader =
                    scope.ServiceProvider.GetRequiredService<MoexReceiverInstrumentReader>();
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                IReadOnlyList<ReceiverInstrument> instruments =
                    await instrumentReader.GetEnabledForDataKindAsync(DataKind, ct);
                if (!_initialized)
                {
                    if (instruments.Count == 0)
                        MoexRealtimeReceiverLogMessages.OrderbookSubscriptionsEmpty(_logger);

                    await PrepareInitialInstrumentsAsync(instruments, coverageWriter, ct);
                    _initialized = true;
                }
                else
                {
                    await ReconcileStatesAsync(instruments, coverageWriter, ct);
                }

                MoexRealtimeRestClient client =
                    scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();
                RealtimeRowWriter<RealtimeOrderbookRowDTO> writer =
                    scope.ServiceProvider.GetRequiredService<RealtimeRowWriter<RealtimeOrderbookRowDTO>>();
                foreach (KeyValuePair<string, ReceiverInstrumentSessionState> pair in _states)
                {
                    if (pair.Value.IsStopping)
                        continue;

                    try
                    {
                        await PollInstrumentAsync(
                            pair.Key,
                            pair.Value,
                            client,
                            writer,
                            coverageWriter,
                            ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        MoexRealtimeReceiverLogMessages.OrderbookInstrumentPollFailed(
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

        private async Task PrepareInitialInstrumentsAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            // Один запрос закрывает ВСЕ осиротевшие 'open'-сеансы стакана прошлого запуска —
            // независимо от инструмента. Приёмник читает только включённые подписки, поэтому
            // точечное закрытие по этому списку пропустило бы осиротевший сеанс отключённой или
            // удалённой подписки. Глобальное закрытие опирается на единственного писателя этого
            // вида данных.
            await coverageWriter.MarkOrphanedOpenAsCrashedAsync(DataKind, null, ct);

            // Открываем сеанс каждому инструменту из читаемого списка. Гейт по crashedClosed не нужен.
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (_states.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await OpenStateAsync(instrument, boardId, coverageWriter, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MoexRealtimeReceiverLogMessages.OrderbookInstrumentPreparationFailed(
                        _logger, ex, instrument.Secid);
                }
            }
        }

        /// <summary>
        /// Приводит словарь состояний к желаемому списку подписок. Снятые инструменты
        /// помечаются к остановке, исключаются из опроса и закрываются штатно; состояние
        /// удаляется только после успешного закрытия, при отказе закрытие повторяется на
        /// следующем обороте. Затем добавляются новые. Пустой желаемый список означает
        /// остановку всех состояний этого вида — это законное состояние, а не сбой.
        /// </summary>
        private async Task ReconcileStatesAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            HashSet<string> desired = new(StringComparer.Ordinal);
            for (int i = 0; i < instruments.Count; i++)
                desired.Add(instruments[i].Secid);

            // Пометка. Изменяем только значения, ключи словаря не трогаем — перечисление безопасно.
            foreach (KeyValuePair<string, ReceiverInstrumentSessionState> pair in _states)
            {
                if (desired.Contains(pair.Key) || pair.Value.IsStopping)
                    continue;

                pair.Value.IsStopping = true;
                MoexRealtimeReceiverLogMessages.OrderbookInstrumentStopping(
                    _logger, pair.Key, pair.Value.SessionId);
            }

            // Ключи собираем заранее: удалять из словаря во время перечисления нельзя.
            List<string> stopping = new();
            foreach (KeyValuePair<string, ReceiverInstrumentSessionState> pair in _states)
            {
                if (pair.Value.IsStopping)
                    stopping.Add(pair.Key);
            }

            for (int i = 0; i < stopping.Count; i++)
            {
                string secid = stopping[i];
                ReceiverInstrumentSessionState state = _states[secid];
                try
                {
                    await coverageWriter.CloseSessionAsync(state.SessionId, state.RowsTotal, ct);
                    _states.Remove(secid);
                    MoexRealtimeReceiverLogMessages.OrderbookInstrumentStopped(
                        _logger, secid, state.SessionId, state.RowsTotal);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Состояние остаётся с признаком остановки: в опрос не попадёт,
                    // закрытие повторится на следующем обороте.
                    MoexRealtimeReceiverLogMessages.OrderbookSessionCloseFailed(
                        _logger, ex, secid, state.SessionId);
                }
            }

            await AddNewInstrumentsAsync(instruments, coverageWriter, ct);
        }

        private async Task AddNewInstrumentsAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (_states.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await coverageWriter.CloseCrashedAsync(
                        instrument.Secid, instrument.Market, boardId, DataKind, null, ct);
                    await OpenStateAsync(instrument, boardId, coverageWriter, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MoexRealtimeReceiverLogMessages.OrderbookInstrumentPreparationFailed(
                        _logger, ex, instrument.Secid);
                }
            }
        }

        private async Task OpenStateAsync(
            ReceiverInstrument instrument,
            string boardId,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            long sessionId = await coverageWriter.OpenSessionAsync(
                instrument.Secid, instrument.Market, boardId, DataKind, null, ct);
            long heartbeatTimestamp = Stopwatch.GetTimestamp();
            _states.Add(
                instrument.Secid,
                new ReceiverInstrumentSessionState(
                    sessionId, instrument.Market, boardId, heartbeatTimestamp));
            MoexRealtimeReceiverLogMessages.OrderbookInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private async Task PollInstrumentAsync(
            string secid,
            ReceiverInstrumentSessionState state,
            MoexRealtimeRestClient client,
            RealtimeRowWriter<RealtimeOrderbookRowDTO> writer,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            // Снимок стакана — под собственным бюджетом, живущим строго вокруг вызова клиента и
            // уничтожаемым до фиксации. Причины те же, что в опросе сделок.
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeOrderbookPoll,
                MoexDataKinds.Orderbook,
                state.Market,
                MoexFlows.Realtime);

            StorageInsertContext insertContext = new StorageInsertContext(
                operationTags.DataKind,
                operationTags.Market,
                MoexFlows.Realtime);

            RealtimeOrderbookParseResult result;
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
                    if (state.Market == StockMarket)
                        result = await client.GetOrderbookStockAsync(secid, fetchCts.Token);
                    else
                        result = await client.GetOrderbookFuturesAsync(secid, fetchCts.Token);

                    MoexMetrics.RowsReceived.Add(
                        result.Rows.Count,
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
                    // Остановка хоста — не отказ источника.
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error);
                    pollActivity?.SetStatus(ActivityStatusCode.Error);
                    // Истёк собственный бюджет получения инструмента. Это отказ по тайм-ауту,
                    // а не отмена: хост работает, оборот продолжается со следующего инструмента.
                    MoexRealtimeReceiverLogMessages.OrderbookInstrumentFetchTimedOut(
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
                if (result.Rows.Count > 0)
                {
                    // Фиксация — под хостовым commitCt. У стакана курсора нет, durable-запись — только
                    // ClickHouse; за ней сразу двигается накопленный счётчик состояния.
                    string? sessionDate = result.DataVersion?.TradeSessionDate;
                    await writer.WriteAsync(
                        secid, result.Rows, sessionDate, insertContext, commitCt);

                    state.RowsTotal += result.Rows.Count;
                    DateTime snapshotSourceTime =
                        MoexClickHouseTime.BuildSourceTimeFromSeqNum(result.Rows[0].SeqNum);
                    state.LastConfirmedMarketTime = MoexTime.ToDateTimeOffset(snapshotSourceTime);
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

        private void PublishTelemetrySnapshots()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> snapshots =
                new List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>>(2);

            AddTelemetrySnapshot(MoexMarkets.Stock, now, snapshots);
            AddTelemetrySnapshot(MoexMarkets.Futures, now, snapshots);

            RealtimeTelemetryState.ReplaceForDataKind(MoexDataKinds.Orderbook, snapshots);
        }

        private void AddTelemetrySnapshot(
            string market,
            DateTimeOffset now,
            List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> snapshots)
        {
            DateTimeOffset? lastSuccessfulPollTime = null;
            DateTimeOffset? lastConfirmedMarketTime = null;
            long activeInstruments = 0;
            long staleInstruments = 0;

            foreach (KeyValuePair<string, ReceiverInstrumentSessionState> pair in _states)
            {
                ReceiverInstrumentSessionState state = pair.Value;
                if (state.IsStopping || state.Market != market)
                    continue;

                activeInstruments++;

                DateTimeOffset? successfulPollTime = state.LastSuccessfulPollTime;
                if (successfulPollTime is null ||
                    now - successfulPollTime.Value > _stalePollThreshold)
                {
                    staleInstruments++;
                }

                if (successfulPollTime is not null &&
                    (lastSuccessfulPollTime is null ||
                     successfulPollTime.Value > lastSuccessfulPollTime.Value))
                {
                    lastSuccessfulPollTime = successfulPollTime;
                }

                DateTimeOffset? confirmedMarketTime = state.LastConfirmedMarketTime;
                if (confirmedMarketTime is not null &&
                    (lastConfirmedMarketTime is null ||
                     confirmedMarketTime.Value > lastConfirmedMarketTime.Value))
                {
                    lastConfirmedMarketTime = confirmedMarketTime;
                }
            }

            if (activeInstruments == 0)
                return;

            snapshots.Add(new KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>(
                new RealtimeTelemetryKey(MoexDataKinds.Orderbook, market),
                new RealtimeTelemetrySnapshot(
                    lastSuccessfulPollTime,
                    lastConfirmedMarketTime,
                    activeInstruments,
                    staleInstruments)));
        }

        protected override async Task CloseSessionsAsync()
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                foreach (KeyValuePair<string, ReceiverInstrumentSessionState> pair in _states)
                {
                    try
                    {
                        await coverageWriter.CloseSessionAsync(
                            pair.Value.SessionId,
                            pair.Value.RowsTotal,
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        MoexRealtimeReceiverLogMessages.OrderbookSessionCloseFailed(
                            _logger, ex, pair.Key, pair.Value.SessionId);
                    }
                }
            }
            catch (Exception ex)
            {
                MoexRealtimeReceiverLogMessages.OrderbookShutdownFailed(_logger, ex);
            }
        }

        private static string GetBoardId(string market)
        {
            if (market == StockMarket)
                return StockBoardId;
            if (market == FuturesMarket)
                return FuturesBoardId;

            throw new InvalidOperationException(
                $"Неизвестный рынок инструмента приёмника: '{market}'.");
        }
    }

}
