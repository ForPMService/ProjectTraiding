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
    /// Периодический приём полных снимков стакана по всем инструментам. Стакан ведёт только
    /// покрытие: курсора у полного снимка нет и в moex_stream_cursors он не записывается.
    /// </summary>
    public sealed class OrderbookReceiverService : BackgroundService
    {
        private const string StockMarket = "stock";
        private const string FuturesMarket = "futures";
        private const string StockBoardId = "TQBR";
        private const string FuturesBoardId = "RFUD";
        private const string DataKind = "orderbook";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderbookReceiverService> _logger;
        private readonly TimeSpan _pollInterval;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly Dictionary<string, OrderbookInstrumentState> _states =
            new Dictionary<string, OrderbookInstrumentState>();
        private bool _initialized;

        public OrderbookReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<OrderbookReceiverService> logger,
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
            MoexRealtimeReceiverLogMessages.OrderbookStarted(_logger, _pollInterval);

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
                        MoexRealtimeReceiverLogMessages.OrderbookTurnFailed(_logger, ex);
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
                MoexRealtimeReceiverLogMessages.OrderbookStopped(_logger);
            }
        }

        private async Task RunTurnAsync(CancellationToken ct)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

            MoexReceiverInstrumentReader instrumentReader =
                scope.ServiceProvider.GetRequiredService<MoexReceiverInstrumentReader>();
            StreamCoverageWriter coverageWriter =
                scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

            IReadOnlyList<ReceiverInstrument> instruments = await instrumentReader.GetAllAsync(ct);
            if (!_initialized)
            {
                if (instruments.Count == 0)
                    MoexRealtimeReceiverLogMessages.OrderbookCatalogEmpty(_logger);

                await PrepareInitialInstrumentsAsync(instruments, coverageWriter, ct);
                _initialized = true;
            }
            else
            {
                await AddNewInstrumentsAsync(instruments, coverageWriter, ct);
            }

            MoexRealtimeRestClient client =
                scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();
            RealtimeRowWriter<RealtimeOrderbookRowDTO> writer =
                scope.ServiceProvider.GetRequiredService<RealtimeRowWriter<RealtimeOrderbookRowDTO>>();
            RealtimeLatestWriter latestWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeLatestWriter>();

            foreach (KeyValuePair<string, OrderbookInstrumentState> pair in _states)
            {
                try
                {
                    await PollInstrumentAsync(
                        pair.Key,
                        pair.Value,
                        client,
                        writer,
                        latestWriter,
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

        private async Task PrepareInitialInstrumentsAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            HashSet<string> crashedClosed = new HashSet<string>();

            // Сначала закрываются все осиротевшие сеансы прошлого запуска.
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
                    MoexRealtimeReceiverLogMessages.OrderbookInstrumentPreparationFailed(
                        _logger, ex, instrument.Secid);
                }
            }

            // Новые сеансы открываются только после полного прохода закрытия.
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (!crashedClosed.Contains(instrument.Secid) || _states.ContainsKey(instrument.Secid))
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
                        instrument.Secid, instrument.Market, boardId, DataKind, ct);
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
                instrument.Secid, instrument.Market, boardId, DataKind, ct);
            _states.Add(
                instrument.Secid,
                new OrderbookInstrumentState(sessionId, instrument.Market, boardId));
            MoexRealtimeReceiverLogMessages.OrderbookInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private async Task PollInstrumentAsync(
            string secid,
            OrderbookInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeRowWriter<RealtimeOrderbookRowDTO> writer,
            RealtimeLatestWriter latestWriter,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            // Снимок стакана — под собственным бюджетом, живущим строго вокруг вызова клиента и
            // уничтожаемым до фиксации. Причины те же, что в PollStockAsync.
            RealtimeOrderbookParseResult result;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                try
                {
                    if (state.Market == StockMarket)
                        result = await client.GetOrderbookStockAsync(secid, fetchCts.Token);
                    else
                        result = await client.GetOrderbookFuturesAsync(secid, fetchCts.Token);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    MoexRealtimeReceiverLogMessages.OrderbookInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    return;
                }
            }

            if (result.Rows.Count == 0)
                return;

            // Фиксация — под хостовым commitCt. У стакана курсора нет, durable-запись — только
            // ClickHouse; за ней счётчик, сердцебиение, витрина последней (best-effort).
            string? sessionDate = result.DataVersion?.TradeSessionDate;
            await writer.WriteAsync(secid, result.Rows, sessionDate, commitCt);

            long rowsTotal = state.RowsTotal + result.Rows.Count;
            state.RowsTotal = rowsTotal;

            await coverageWriter.HeartbeatAsync(state.SessionId, rowsTotal, commitCt);

            await latestWriter.WriteLatestOrderbookAsync(secid, result.Rows, commitCt);
        }

        private async Task CloseSessionsAsync()
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                foreach (KeyValuePair<string, OrderbookInstrumentState> pair in _states)
                {
                    try
                    {
                        await coverageWriter.CloseSessionAsync(
                            pair.Value.SessionId, CancellationToken.None);
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

    internal sealed class OrderbookInstrumentState
    {
        public OrderbookInstrumentState(long sessionId, string market, string boardId)
        {
            SessionId = sessionId;
            Market = market;
            BoardId = boardId;
        }

        public long SessionId { get; }
        public string Market { get; }
        public string BoardId { get; }
        public long RowsTotal { get; set; }
    }
}
