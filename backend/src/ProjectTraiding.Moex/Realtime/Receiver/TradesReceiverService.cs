using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.Redis;
using System;
using System.Collections.Generic;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём ленты сделок по всем инструментам. Курсор TRADENO и покрытие
    /// ведутся отдельно по каждому инструменту; сбой одного инструмента не отменяет оборот.
    /// </summary>
    public sealed class TradesReceiverService : BackgroundService
    {
        private const string StockMarket = "stock";
        private const string FuturesMarket = "futures";
        private const string StockBoardId = "TQBR";
        private const string FuturesBoardId = "RFUD";
        private const string DataKind = "trades";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TradesReceiverService> _logger;
        private readonly TimeSpan _pollInterval;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly Dictionary<string, TradesInstrumentState> _states =
            new Dictionary<string, TradesInstrumentState>();
        private bool _initialized;

        public TradesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<TradesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
            _instrumentFetchTimeout = instrumentFetchTimeout;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            MoexRealtimeReceiverLogMessages.TradesStarted(_logger, _pollInterval);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RunTurnAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        MoexRealtimeReceiverLogMessages.TradesTurnFailed(_logger, ex);
                    }

                    try
                    {
                        await Task.Delay(_pollInterval, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await CloseSessionsAsync();
                MoexRealtimeReceiverLogMessages.TradesStopped(_logger);
            }
        }

        private async Task RunTurnAsync(CancellationToken ct)
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
                await AddNewInstrumentsAsync(instruments, cursorWriter, coverageWriter, client, ct);
            }

            RealtimeRowWriter<RealtimeTradesStockDTO> stockWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeRowWriter<RealtimeTradesStockDTO>>();
            RealtimeRowWriter<RealtimeTradesFuturesDTO> futuresWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeRowWriter<RealtimeTradesFuturesDTO>>();
            RealtimeLatestWriter latestWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeLatestWriter>();

            foreach (KeyValuePair<string, TradesInstrumentState> pair in _states)
            {
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
            // независимо от инструмента. Точечное закрытие по списку пропускает осиротевший сеанс
            // инструмента вне списка; предстоящий переход на подписки (правка C) расширит это на
            // отключённые подписки. Глобальное закрытие снимает щель заранее и опирается на
            // единственного писателя этого вида данных.
            await coverageWriter.MarkOrphanedOpenAsCrashedAsync(DataKind, ct);

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
                        instrument.Secid, instrument.Market, boardId, DataKind, ct);
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
                instrument.Secid, instrument.Market, boardId, DataKind, ct);

            _states.Add(
                instrument.Secid,
                new TradesInstrumentState(
                    initialAfterTradeNo,
                    sessionId,
                    instrument.Market,
                    boardId));
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
            RealtimeTradesParseResult<RealtimeTradesStockDTO> result;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                try
                {
                    result = await client.GetTradesStockAsync(
                        secid, state.AfterTradeNo, cancellationToken: fetchCts.Token);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    MoexRealtimeReceiverLogMessages.TradesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    return;
                }
            }

            if (result.Rows.Count == 0)
                return;

            // Фиксация страницы — под хостовым commitCt. Обрыв бюджетом между записью и курсором
            // недопустим (иначе следующий оборот со старым курсором получит РАСШИРЕННУЮ пачку с
            // другим токеном, и MergeTree задублирует уже записанные строки). Порядок: durable-запись,
            // затем сразу in-memory состояние, затем сердцебиение, витрина — последней.
            string? sessionDate = result.DataVersion?.TradeSessionDate;
            await writer.WriteAsync(secid, result.Rows, sessionDate, commitCt);

            RealtimeTradesStockDTO last = result.Rows[^1];
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
            long rowsTotal = state.RowsTotal + result.Rows.Count;
            state.RowsTotal = rowsTotal;
            state.AfterTradeNo = lastTradeNo;

            await coverageWriter.HeartbeatAsync(state.SessionId, rowsTotal, commitCt);

            // Витрина последних значений — best-effort, последней: писатель сам проглатывает сбой.
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
            RealtimeTradesParseResult<RealtimeTradesFuturesDTO> result;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                try
                {
                    result = await client.GetTradesFuturesAsync(
                        secid, state.AfterTradeNo, cancellationToken: fetchCts.Token);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    MoexRealtimeReceiverLogMessages.TradesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    return;
                }
            }

            if (result.Rows.Count == 0)
                return;

            // Фиксация — под хостовым commitCt. Порядок: durable-запись, in-memory состояние,
            // сердцебиение, витрина — последней.
            string? sessionDate = result.DataVersion?.TradeSessionDate;
            await writer.WriteAsync(secid, result.Rows, sessionDate, commitCt);

            RealtimeTradesFuturesDTO last = result.Rows[^1];
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

            long rowsTotal = state.RowsTotal + result.Rows.Count;
            state.RowsTotal = rowsTotal;
            state.AfterTradeNo = lastTradeNo;

            await coverageWriter.HeartbeatAsync(state.SessionId, rowsTotal, commitCt);

            await latestWriter.WriteLatestFuturesTradeAsync(secid, last, commitCt);
        }

        private static async Task<(long TradeNo, DateTime SourceTime)?> TrySeedTailAsync(
            ReceiverInstrument instrument,
            MoexRealtimeRestClient client,
            CancellationToken ct)
        {
            Dictionary<string, string> reversedParams =
                new Dictionary<string, string> { ["reversed"] = "1" };

            if (instrument.Market == StockMarket)
            {
                RealtimeTradesParseResult<RealtimeTradesStockDTO> page =
                    await client.GetTradesStockAsync(instrument.Secid, null, reversedParams, ct);
                return PickTailStock(page.Rows);
            }

            RealtimeTradesParseResult<RealtimeTradesFuturesDTO> futuresPage =
                await client.GetTradesFuturesAsync(instrument.Secid, null, reversedParams, ct);
            return PickTailFutures(futuresPage.Rows);
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

        private async Task CloseSessionsAsync()
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
                            pair.Value.SessionId, CancellationToken.None);
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

    internal sealed class TradesInstrumentState
    {
        public TradesInstrumentState(
            long? afterTradeNo,
            long sessionId,
            string market,
            string boardId)
        {
            AfterTradeNo = afterTradeNo;
            SessionId = sessionId;
            Market = market;
            BoardId = boardId;
        }

        public long? AfterTradeNo { get; set; }
        public long SessionId { get; }
        public string Market { get; }
        public string BoardId { get; }
        public long RowsTotal { get; set; }
    }
}
