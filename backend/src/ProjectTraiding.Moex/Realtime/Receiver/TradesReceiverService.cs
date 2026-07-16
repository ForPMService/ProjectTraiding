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
        private readonly Dictionary<string, TradesInstrumentState> _states =
            new Dictionary<string, TradesInstrumentState>();
        private bool _initialized;

        public TradesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<TradesReceiverService> logger,
            TimeSpan pollInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
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

            IReadOnlyList<ReceiverInstrument> instruments = await instrumentReader.GetAllAsync(ct);
            if (!_initialized)
            {
                if (instruments.Count == 0)
                    MoexRealtimeReceiverLogMessages.TradesCatalogEmpty(_logger);

                await PrepareInitialInstrumentsAsync(
                    instruments, cursorWriter, coverageWriter, ct);
                _initialized = true;
            }
            else
            {
                await AddNewInstrumentsAsync(instruments, cursorWriter, coverageWriter, ct);
            }

            MoexRealtimeRestClient client =
                scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();
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
            CancellationToken ct)
        {
            HashSet<string> crashedClosed = new HashSet<string>();

            // Первый проход закрывает все осиротевшие сеансы до открытия хотя бы одного нового.
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await coverageWriter.CloseCrashedAsync(
                        instrument.Secid, instrument.Market, boardId, DataKind, ct);
                    crashedClosed.Add(instrument.Secid);
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

            // Второй проход читает курсоры и открывает новые сеансы только после первого.
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (!crashedClosed.Contains(instrument.Secid) || _states.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await OpenStateAsync(instrument, boardId, cursorWriter, coverageWriter, ct);
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
                    await OpenStateAsync(instrument, boardId, cursorWriter, coverageWriter, ct);
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
            CancellationToken ct)
        {
            StreamCursorState? cursor = await cursorWriter.TryGetAsync(
                instrument.Secid,
                instrument.Market,
                boardId,
                DataKind,
                null,
                ct);
            long sessionId = await coverageWriter.OpenSessionAsync(
                instrument.Secid, instrument.Market, boardId, DataKind, ct);

            _states.Add(
                instrument.Secid,
                new TradesInstrumentState(
                    cursor?.LastTradeNo,
                    sessionId,
                    instrument.Market,
                    boardId));
            MoexRealtimeReceiverLogMessages.TradesInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private static async Task PollStockAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeRowWriter<RealtimeTradesStockDTO> writer,
            RealtimeLatestWriter latestWriter,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            RealtimeTradesParseResult<RealtimeTradesStockDTO> result =
                await client.GetTradesStockPagedAsync(
                    secid, state.AfterTradeNo, cancellationToken: ct);
            if (result.Rows.Count == 0)
                return;

            string? sessionDate = result.DataVersion?.TradeSessionDate;
            await writer.WriteAsync(secid, result.Rows, sessionDate, ct);

            RealtimeTradesStockDTO last = result.Rows[^1];
            await latestWriter.WriteLatestStockTradeAsync(secid, last, ct);

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
                ct);

            long rowsTotal = state.RowsTotal + result.Rows.Count;
            await coverageWriter.HeartbeatAsync(state.SessionId, rowsTotal, ct);

            state.RowsTotal = rowsTotal;
            state.AfterTradeNo = lastTradeNo;
        }

        private static async Task PollFuturesAsync(
            string secid,
            TradesInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeRowWriter<RealtimeTradesFuturesDTO> writer,
            RealtimeLatestWriter latestWriter,
            StreamCursorWriter cursorWriter,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            RealtimeTradesParseResult<RealtimeTradesFuturesDTO> result =
                await client.GetTradesFuturesPagedAsync(
                    secid, state.AfterTradeNo, cancellationToken: ct);
            if (result.Rows.Count == 0)
                return;

            string? sessionDate = result.DataVersion?.TradeSessionDate;
            await writer.WriteAsync(secid, result.Rows, sessionDate, ct);

            RealtimeTradesFuturesDTO last = result.Rows[^1];
            await latestWriter.WriteLatestFuturesTradeAsync(secid, last, ct);

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
                ct);

            long rowsTotal = state.RowsTotal + result.Rows.Count;
            await coverageWriter.HeartbeatAsync(state.SessionId, rowsTotal, ct);

            state.RowsTotal = rowsTotal;
            state.AfterTradeNo = lastTradeNo;
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
