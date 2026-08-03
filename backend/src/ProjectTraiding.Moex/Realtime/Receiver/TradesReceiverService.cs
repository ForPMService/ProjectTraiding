using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём ленты сделок по всем инструментам. Курсор TRADENO и покрытие
    /// ведутся отдельно по каждому инструменту; сбой одного инструмента не отменяет оборот.
    /// </summary>
    public sealed class TradesReceiverService : RealtimeReceiverServiceBase
    {
        private const string StockMarket = "stock";
        private const string FuturesMarket = "futures";
        private const string StockBoardId = "TQBR";
        private const string FuturesBoardId = "RFUD";
        private const string DataKind = "trades";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TradesReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _heartbeatMinInterval;
        private readonly Dictionary<string, TradesInstrumentState> _states =
            new Dictionary<string, TradesInstrumentState>();
        private bool _initialized;

        public TradesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<TradesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout,
            TimeSpan heartbeatMinInterval)
            : base(pollInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _instrumentFetchTimeout = instrumentFetchTimeout;
            _heartbeatMinInterval = heartbeatMinInterval;
        }

        protected override void LogStarted(TimeSpan pollInterval)
            => MoexRealtimeReceiverLogMessages.TradesStarted(_logger, pollInterval);

        protected override void LogTurnFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.TradesTurnFailed(_logger, exception);

        protected override void LogStopped()
            => MoexRealtimeReceiverLogMessages.TradesStopped(_logger);

        protected override async Task RunTurnAsync(CancellationToken ct)
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
                    instruments, cursorWriter, coverageWriter, client, ct);
                _initialized = true;
            }
            else
            {
                await ReconcileStatesAsync(
                    instruments, cursorWriter, coverageWriter, client, ct);
            }

            RealtimeRowWriter<RealtimeTradesStockDTO> stockWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeRowWriter<RealtimeTradesStockDTO>>();
            RealtimeRowWriter<RealtimeTradesFuturesDTO> futuresWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeRowWriter<RealtimeTradesFuturesDTO>>();
            RealtimeLatestWriter latestWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeLatestWriter>();

            foreach (KeyValuePair<string, TradesInstrumentState> pair in _states)
            {
                if (pair.Value.IsStopping)
                    continue;

                try
                {
                    if (pair.Value.Market == StockMarket)
                    {
                        await PollStockAsync(
                            pair.Key,
                            pair.Value,
                            client,
                            stockWriter,
                            latestWriter,
                            cursorWriter,
                            coverageWriter,
                            ct);
                    }
                    else
                    {
                        await PollFuturesAsync(
                            pair.Key,
                            pair.Value,
                            client,
                            futuresWriter,
                            latestWriter,
                            cursorWriter,
                            coverageWriter,
                            ct);
                    }
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

        private async Task PrepareInitialInstrumentsAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            MoexRealtimeRestClient client,
            CancellationToken ct)
        {
            // Один запрос закрывает ВСЕ осиротевшие 'open'-сеансы сделок прошлого запуска —
            // независимо от инструмента. Приёмник читает только включённые подписки, поэтому
            // точечное закрытие по этому списку пропустило бы осиротевший сеанс отключённой или
            // удалённой подписки. Глобальное закрытие опирается на единственного писателя этого
            // вида данных.
            await coverageWriter.MarkOrphanedOpenAsCrashedAsync(DataKind, null, ct);

            // Открываем сеанс каждому инструменту из читаемого списка. Прежнего гейта по crashedClosed
            // нет — все старые 'open'-строки закрыты выше, поверх ничего не наслоится.
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (_states.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await OpenStateAsync(instrument, boardId, cursorWriter, coverageWriter, client, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MoexRealtimeReceiverLogMessages.TradesInstrumentPreparationFailed(
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
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            MoexRealtimeRestClient client,
            CancellationToken ct)
        {
            HashSet<string> desired = new(StringComparer.Ordinal);
            for (int i = 0; i < instruments.Count; i++)
                desired.Add(instruments[i].Secid);

            // Пометка. Изменяем только значения, ключи словаря не трогаем — перечисление безопасно.
            foreach (KeyValuePair<string, TradesInstrumentState> pair in _states)
            {
                if (desired.Contains(pair.Key) || pair.Value.IsStopping)
                    continue;

                pair.Value.IsStopping = true;
                MoexRealtimeReceiverLogMessages.TradesInstrumentStopping(
                    _logger, pair.Key, pair.Value.SessionId);
            }

            // Ключи собираем заранее: удалять из словаря во время перечисления нельзя.
            List<string> stopping = new();
            foreach (KeyValuePair<string, TradesInstrumentState> pair in _states)
            {
                if (pair.Value.IsStopping)
                    stopping.Add(pair.Key);
            }

            for (int i = 0; i < stopping.Count; i++)
            {
                string secid = stopping[i];
                TradesInstrumentState state = _states[secid];
                try
                {
                    await coverageWriter.CloseSessionAsync(state.SessionId, state.RowsTotal, ct);
                    _states.Remove(secid);
                    MoexRealtimeReceiverLogMessages.TradesInstrumentStopped(
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
                    MoexRealtimeReceiverLogMessages.TradesSessionCloseFailed(
                        _logger, ex, secid, state.SessionId);
                }
            }

            await AddNewInstrumentsAsync(
                instruments, cursorWriter, coverageWriter, client, ct);
        }

        private async Task AddNewInstrumentsAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            MoexRealtimeRestClient client,
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
                    await OpenStateAsync(instrument, boardId, cursorWriter, coverageWriter, client, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MoexRealtimeReceiverLogMessages.TradesInstrumentPreparationFailed(
                        _logger, ex, instrument.Secid);
                }
            }
        }

        private async Task OpenStateAsync(
            ReceiverInstrument instrument,
            string boardId,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            MoexRealtimeRestClient client,
            CancellationToken ct)
        {
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
                    await cursorWriter.UpsertAsync(
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

            _states.Add(
                instrument.Secid,
                new TradesInstrumentState(
                    initialAfterTradeNo,
                    sessionId,
                    instrument.Market,
                    boardId,
                    heartbeatTimestamp));
            MoexRealtimeReceiverLogMessages.TradesInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private async Task PollStockAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeRowWriter<RealtimeTradesStockDTO> writer,
            RealtimeLatestWriter latestWriter,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            // Получение одной страницы — под собственным бюджетом, живущим строго вокруг вызова
            // клиента. По истечении RealtimeInstrumentFetchTimeout получение отменяется, инструмент
            // пропускается до следующего оборота. Бюджет уничтожается ЗДЕСЬ, до фиксации: иначе его
            // таймер, сработав во время медленной записи, мог бы ошибочно попасть на исключение
            // фазы фиксации и выдать сбой хранилища за тайм-аут получения.
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeTradesPoll,
                MoexDataKinds.Trades,
                state.Market);

            long fetchStart = Stopwatch.GetTimestamp();

            RealtimeTradesParseResult<RealtimeTradesStockDTO> result;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                try
                {
                    result = await client.GetTradesStockAsync(
                        secid, state.AfterTradeNo, cancellationToken: fetchCts.Token);
                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    MoexRealtimeReceiverLogMessages.TradesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    MoexMetrics.RecordOperationTimeout(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    return;
                }
                catch (Exception ex)
                {
                    MoexMetrics.RecordOperationError(
                        in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    throw;
                }
            }

            RealtimeTradesStockDTO? last = null;
            if (result.Rows.Count > 0)
            {
                // Фиксация страницы — под хостовым commitCt. Обрыв бюджетом между записью и курсором
                // недопустим: следующий оборот со старым курсором получит расширенную пачку с другим
                // токеном, insert-дедупликация её примет, и до фонового слияния будут видны лишние
                // физические версии. ReplacingMergeTree схлопнет их по ключу при слиянии; чтение до
                // этого требует FINAL. Порядок: durable-запись, затем сразу in-memory состояние.
                string? sessionDate = result.DataVersion?.TradeSessionDate;
                await writer.WriteAsync(secid, result.Rows, sessionDate, commitCt);

                last = result.Rows[^1];
                long lastTradeNo = last.TradeNo!.Value;
                DateTime lastSourceTime =
                    MoexClickHouseTime.BuildSourceTime(last.TradeDate, last.TradeTime);
                await cursorWriter.UpsertAsync(
                    secid,
                    state.Market,
                    state.BoardId,
                    DataKind,
                    null,
                    lastSourceTime,
                    lastTradeNo,
                    commitCt);

                // In-memory состояние двигается сразу за durable-курсором — чтобы не разойтись с ним,
                // если сердцебиение или витрина упадут.
                state.RowsTotal += result.Rows.Count;
                state.AfterTradeNo = lastTradeNo;
            }

            await ReceiverSessionHeartbeat.WriteIfDueAsync(
                state, _heartbeatMinInterval, coverageWriter, commitCt);

            // Витрина последних значений — best-effort, последней: писатель сам проглатывает сбой.
            if (last is not null)
                await latestWriter.WriteLatestStockTradeAsync(secid, last, commitCt);
        }

        private async Task PollFuturesAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeRowWriter<RealtimeTradesFuturesDTO> writer,
            RealtimeLatestWriter latestWriter,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            // Получение одной страницы — под собственным бюджетом, живущим строго вокруг вызова
            // клиента и уничтожаемым до фиксации. Причины те же, что в PollStockAsync.
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeTradesPoll,
                MoexDataKinds.Trades,
                state.Market);

            long fetchStart = Stopwatch.GetTimestamp();

            RealtimeTradesParseResult<RealtimeTradesFuturesDTO> result;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                try
                {
                    result = await client.GetTradesFuturesAsync(
                        secid, state.AfterTradeNo, cancellationToken: fetchCts.Token);
                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    MoexRealtimeReceiverLogMessages.TradesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    MoexMetrics.RecordOperationTimeout(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    return;
                }
                catch (Exception ex)
                {
                    MoexMetrics.RecordOperationError(
                        in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    throw;
                }
            }

            RealtimeTradesFuturesDTO? last = null;
            if (result.Rows.Count > 0)
            {
                // Фиксация — под хостовым commitCt. Порядок: durable-запись и in-memory состояние.
                string? sessionDate = result.DataVersion?.TradeSessionDate;
                await writer.WriteAsync(secid, result.Rows, sessionDate, commitCt);

                last = result.Rows[^1];
                long lastTradeNo = last.TradeNo!.Value;
                DateTime lastSourceTime =
                    MoexClickHouseTime.BuildSourceTime(last.TradeDate, last.TradeTime);
                await cursorWriter.UpsertAsync(
                    secid,
                    state.Market,
                    state.BoardId,
                    DataKind,
                    null,
                    lastSourceTime,
                    lastTradeNo,
                    commitCt);

                state.RowsTotal += result.Rows.Count;
                state.AfterTradeNo = lastTradeNo;
            }

            await ReceiverSessionHeartbeat.WriteIfDueAsync(
                state, _heartbeatMinInterval, coverageWriter, commitCt);

            if (last is not null)
                await latestWriter.WriteLatestFuturesTradeAsync(secid, last, commitCt);
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
                instrument.Market);

            long fetchStart = Stopwatch.GetTimestamp();

            try
            {
                if (instrument.Market == StockMarket)
                {
                    RealtimeTradesParseResult<RealtimeTradesStockDTO> page =
                        await client.GetTradesStockAsync(instrument.Secid, null, reversedParams, ct);
                    (long TradeNo, DateTime SourceTime)? tail = PickTailStock(page.Rows);
                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    return tail;
                }

                RealtimeTradesParseResult<RealtimeTradesFuturesDTO> futuresPage =
                    await client.GetTradesFuturesAsync(instrument.Secid, null, reversedParams, ct);
                (long TradeNo, DateTime SourceTime)? futuresTail = PickTailFutures(futuresPage.Rows);
                MoexMetrics.RecordOperationSuccess(
                    in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                return futuresTail;
            }
            catch (OperationCanceledException)
            {
                MoexMetrics.RecordOperationCancelled(
                    in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                throw;
            }
            catch (Exception ex)
            {
                MoexMetrics.RecordOperationError(
                    in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                throw;
            }
        }

        private static (long TradeNo, DateTime SourceTime)? PickTailStock(
            List<RealtimeTradesStockDTO> rows)
        {
            long maxTradeNo = long.MinValue;
            RealtimeTradesStockDTO? tail = null;
            for (int i = 0; i < rows.Count; i++)
            {
                long? tradeNo = rows[i].TradeNo;
                if (tradeNo is not null && tradeNo.Value > maxTradeNo)
                {
                    maxTradeNo = tradeNo.Value;
                    tail = rows[i];
                }
            }

            if (tail is null)
                return null;

            return (maxTradeNo, MoexClickHouseTime.BuildSourceTime(tail.TradeDate, tail.TradeTime));
        }

        private static (long TradeNo, DateTime SourceTime)? PickTailFutures(
            List<RealtimeTradesFuturesDTO> rows)
        {
            long maxTradeNo = long.MinValue;
            RealtimeTradesFuturesDTO? tail = null;
            for (int i = 0; i < rows.Count; i++)
            {
                long? tradeNo = rows[i].TradeNo;
                if (tradeNo is not null && tradeNo.Value > maxTradeNo)
                {
                    maxTradeNo = tradeNo.Value;
                    tail = rows[i];
                }
            }

            if (tail is null)
                return null;

            return (maxTradeNo, MoexClickHouseTime.BuildSourceTime(tail.TradeDate, tail.TradeTime));
        }

        protected override async Task CloseSessionsAsync()
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                foreach (KeyValuePair<string, TradesInstrumentState> pair in _states)
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
                        MoexRealtimeReceiverLogMessages.TradesSessionCloseFailed(
                            _logger, ex, pair.Key, pair.Value.SessionId);
                    }
                }
            }
            catch (Exception ex)
            {
                MoexRealtimeReceiverLogMessages.TradesShutdownFailed(_logger, ex);
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

    internal sealed class TradesInstrumentState : ReceiverInstrumentSessionState
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
