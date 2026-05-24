using History_DataMoex.Clients;
using History_DataMoex.Contracts.Dto.Algopack;
using History_DataMoex.Contracts.Dto.Realtime;

namespace History_DataMoex.Endpoints
{
    /// <summary>
    /// Диагностические endpoint-ы ручной проверки real-time REST MOEX (Шаг 9).
    /// Полноценный poller будет отдельным компонентом.
    /// 
    /// Одноразовые endpoint-ы для проверки:
    ///   — обновляется ли snapshot стакана между запросами;
    ///   — приходят ли новые сделки, есть ли дубли;
    ///   — что лучше как ключ догрузки: TRADENO или RECNO;
    ///   — меняется ли незакрытая свеча.
    /// 
    /// Запускаются вручную оператором. Результат — диагностический отчёт.
    /// </summary>
    public static class RealtimeDiagnosticEndpoints
    {
        /// <summary>
        /// Диагностические endpoint-ы ручной проверки real-time REST MOEX (Шаг 9).
        /// Полноценный poller будет отдельным компонентом.
        /// 
        /// Одноразовые endpoint-ы для проверки:
        ///   — обновляется ли snapshot стакана между запросами;
        ///   — приходят ли новые сделки, есть ли дубли;
        ///   — что лучше как ключ догрузки: TRADENO или RECNO;
        ///   — меняется ли незакрытая свеча.
        /// 
        /// Запускаются вручную оператором. Результат — диагностический отчёт.
        /// </summary>


        // ═══════════════════════════════════════════════════════════
        // Response records
        // ═══════════════════════════════════════════════════════════

        public sealed record OrderbookPollReport(
        int PauseMs,
        OrderbookSnapshot Request1,
        OrderbookSnapshot Request2,
        bool SeqNumChanged,
        bool UpdateTimeChanged,
        bool DataVersionChanged,
        bool RowCountChanged);

        public sealed record OrderbookSnapshot(
            int RowCount,
            long? SeqNum,
            string? UpdateTime,
            int? DataVersion,
            long? DataVersionSeqNum,
            double? BestBid,
            double? BestAsk);

        public sealed record TradesPollReport(
            string Market,
            int PauseMs,
            TradesSnapshot Request1,
            TradesSnapshot Request2,
            int NewTradesCount,
            int DuplicateTradeNos,
            bool TradeNoOrderPreserved,
            bool DataVersionChanged);

        public sealed record TradesSnapshot(
            int RowCount,
            long? FirstTradeNo,
            long? LastTradeNo,
            long? FirstRecNo,
            long? LastRecNo,
            int? DataVersion,
            long? DataVersionSeqNum);

        public sealed record CandlesPollReport(
            int PauseMs,
            CandlesSnapshot Request1,
            CandlesSnapshot Request2,
            bool LastCandleChanged,
            bool RowCountChanged);

        public sealed record CandlesSnapshot(
            int RowCount,
            string? LastBegin,
            double? LastOpen,
            double? LastClose,
            double? LastHigh,
            double? LastLow,
            double? LastVolume);

        // ═══════════════════════════════════════════════════════════
        // Route registration
        // ═══════════════════════════════════════════════════════════

        public static IEndpointRouteBuilder MapRealtimeDiagnosticEndpoints(this IEndpointRouteBuilder routes)
        {
            RouteGroupBuilder group = routes.MapGroup("/debug/realtime/poll");

            group.MapGet("/{scenario}/{ticker}", PollRealtimeAsync);

            return routes;
        }

        // ═══════════════════════════════════════════════════════════
        // Router
        // ═══════════════════════════════════════════════════════════

        private static async Task<IResult> PollRealtimeAsync(
            string scenario,
            string ticker,
            MoexRealtimeRestClient client,
            int? pauseMs,
            CancellationToken ct)
        {
            switch (scenario)
            {
                case "orderbook-stock":
                    return await PollOrderbookStockAsync(ticker, client, pauseMs, ct);

                case "orderbook-futures":
                    return await PollOrderbookFuturesAsync(ticker, client, pauseMs, ct);

                case "trades-stock":
                    return await PollTradesStockAsync(ticker, client, pauseMs, ct);

                case "trades-futures":
                    return await PollTradesFuturesAsync(ticker, client, pauseMs, ct);

                case "candles-today-stock":
                    return await PollCandlesTodayStockAsync(ticker, client, pauseMs, ct);

                case "candles-today-futures":
                    return await PollCandlesTodayFuturesAsync(ticker, client, pauseMs, ct);

                default:
                    return Results.BadRequest(
                        "Unknown realtime poll scenario. Allowed values: " +
                        "orderbook-stock, orderbook-futures, trades-stock, trades-futures, " +
                        "candles-today-stock, candles-today-futures.");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Handlers
        // ═══════════════════════════════════════════════════════════

        private static async Task<IResult> PollOrderbookStockAsync(
            string ticker,
            MoexRealtimeRestClient client,
            int? pauseMs,
            CancellationToken ct)
        {
            int effectivePauseMs = pauseMs ?? 1000;

            IResult? pauseError = ValidatePauseMs(effectivePauseMs);
            if (pauseError is not null) return pauseError;

            RealtimeOrderbookParseResult r1 = await client.GetOrderbookStockAsync(ticker, ct);
            await Task.Delay(effectivePauseMs, ct);
            RealtimeOrderbookParseResult r2 = await client.GetOrderbookStockAsync(ticker, ct);

            return Results.Ok(BuildOrderbookReport(r1, r2, effectivePauseMs));
        }

        private static async Task<IResult> PollOrderbookFuturesAsync(
            string ticker,
            MoexRealtimeRestClient client,
            int? pauseMs,
            CancellationToken ct)
        {
            int effectivePauseMs = pauseMs ?? 1000;

            IResult? pauseError = ValidatePauseMs(effectivePauseMs);
            if (pauseError is not null) return pauseError;

            RealtimeOrderbookParseResult r1 = await client.GetOrderbookFuturesAsync(ticker, ct);
            await Task.Delay(effectivePauseMs, ct);
            RealtimeOrderbookParseResult r2 = await client.GetOrderbookFuturesAsync(ticker, ct);

            return Results.Ok(BuildOrderbookReport(r1, r2, effectivePauseMs));
        }

        private static async Task<IResult> PollTradesStockAsync(
            string ticker,
            MoexRealtimeRestClient client,
            int? pauseMs,
            CancellationToken ct)
        {
            int effectivePauseMs = pauseMs ?? 2000;

            IResult? pauseError = ValidatePauseMs(effectivePauseMs);
            if (pauseError is not null) return pauseError;

            RealtimeTradesParseResult<RealtimeTradesStockDTO> r1 =
                await client.GetTradesStockAsync(ticker, cancellationToken: ct);
            await Task.Delay(effectivePauseMs, ct);
            RealtimeTradesParseResult<RealtimeTradesStockDTO> r2 =
                await client.GetTradesStockAsync(ticker, cancellationToken: ct);

            TradesSnapshot snap1 = BuildTradesSnapshot(
                r1.Rows.Count, r1.DataVersion,
                r1.Rows.Count > 0 ? r1.Rows[0].TradeNo : null,
                r1.Rows.Count > 0 ? r1.Rows[^1].TradeNo : null,
                null, null);

            TradesSnapshot snap2 = BuildTradesSnapshot(
                r2.Rows.Count, r2.DataVersion,
                r2.Rows.Count > 0 ? r2.Rows[0].TradeNo : null,
                r2.Rows.Count > 0 ? r2.Rows[^1].TradeNo : null,
                null, null);

            HashSet<long> tradeNos1 = new HashSet<long>();
            for (int i = 0; i < r1.Rows.Count; i++)
            {
                long? tradeNo = r1.Rows[i].TradeNo;
                if (tradeNo.HasValue)
                    tradeNos1.Add(tradeNo.Value);
            }

            List<long> tradeNos2 = new List<long>();
            for (int i = 0; i < r2.Rows.Count; i++)
            {
                long? tradeNo = r2.Rows[i].TradeNo;
                if (tradeNo.HasValue)
                    tradeNos2.Add(tradeNo.Value);
            }

            int duplicates = CountDuplicates(tradeNos2, tradeNos1);
            int newTrades = tradeNos2.Count - duplicates;
            bool orderPreserved = IsAscending(tradeNos2);

            return Results.Ok(new TradesPollReport(
                Market: "stock",
                PauseMs: effectivePauseMs,
                Request1: snap1,
                Request2: snap2,
                NewTradesCount: newTrades,
                DuplicateTradeNos: duplicates,
                TradeNoOrderPreserved: orderPreserved,
                DataVersionChanged: snap1.DataVersion != snap2.DataVersion
                    || snap1.DataVersionSeqNum != snap2.DataVersionSeqNum));
        }

        private static async Task<IResult> PollTradesFuturesAsync(
            string ticker,
            MoexRealtimeRestClient client,
            int? pauseMs,
            CancellationToken ct)
        {
            int effectivePauseMs = pauseMs ?? 2000;

            IResult? pauseError = ValidatePauseMs(effectivePauseMs);
            if (pauseError is not null) return pauseError;

            RealtimeTradesParseResult<RealtimeTradesFuturesDTO> r1 =
                await client.GetTradesFuturesAsync(ticker, cancellationToken: ct);
            await Task.Delay(effectivePauseMs, ct);
            RealtimeTradesParseResult<RealtimeTradesFuturesDTO> r2 =
                await client.GetTradesFuturesAsync(ticker, cancellationToken: ct);

            TradesSnapshot snap1 = BuildTradesSnapshot(
                r1.Rows.Count, r1.DataVersion,
                r1.Rows.Count > 0 ? r1.Rows[0].TradeNo : null,
                r1.Rows.Count > 0 ? r1.Rows[^1].TradeNo : null,
                r1.Rows.Count > 0 ? r1.Rows[0].RecNo : null,
                r1.Rows.Count > 0 ? r1.Rows[^1].RecNo : null);

            TradesSnapshot snap2 = BuildTradesSnapshot(
                r2.Rows.Count, r2.DataVersion,
                r2.Rows.Count > 0 ? r2.Rows[0].TradeNo : null,
                r2.Rows.Count > 0 ? r2.Rows[^1].TradeNo : null,
                r2.Rows.Count > 0 ? r2.Rows[0].RecNo : null,
                r2.Rows.Count > 0 ? r2.Rows[^1].RecNo : null);

            HashSet<long> tradeNos1 = new HashSet<long>();
            for (int i = 0; i < r1.Rows.Count; i++)
            {
                long? tradeNo = r1.Rows[i].TradeNo;
                if (tradeNo.HasValue)
                    tradeNos1.Add(tradeNo.Value);
            }

            List<long> tradeNos2 = new List<long>();
            for (int i = 0; i < r2.Rows.Count; i++)
            {
                long? tradeNo = r2.Rows[i].TradeNo;
                if (tradeNo.HasValue)
                    tradeNos2.Add(tradeNo.Value);
            }

            int duplicates = CountDuplicates(tradeNos2, tradeNos1);
            int newTrades = tradeNos2.Count - duplicates;
            bool orderPreserved = IsAscending(tradeNos2);

            return Results.Ok(new TradesPollReport(
                Market: "futures",
                PauseMs: effectivePauseMs,
                Request1: snap1,
                Request2: snap2,
                NewTradesCount: newTrades,
                DuplicateTradeNos: duplicates,
                TradeNoOrderPreserved: orderPreserved,
                DataVersionChanged: snap1.DataVersion != snap2.DataVersion
                    || snap1.DataVersionSeqNum != snap2.DataVersionSeqNum));
        }

        private static async Task<IResult> PollCandlesTodayStockAsync(
            string ticker,
            MoexRealtimeRestClient client,
            int? pauseMs,
            CancellationToken ct)
        {
            int effectivePauseMs = pauseMs ?? 3000;
            DateOnly tradeDate = DateOnly.FromDateTime(DateTime.Today);

            IResult? pauseError = ValidatePauseMs(effectivePauseMs);
            if (pauseError is not null) return pauseError;

            List<CandlesDTO> r1 = await client.GetCandlesTodayStockAsync(
                ticker, tradeDate, interval: 1, cancellationToken: ct);
            await Task.Delay(effectivePauseMs, ct);
            List<CandlesDTO> r2 = await client.GetCandlesTodayStockAsync(
                ticker, tradeDate, interval: 1, cancellationToken: ct);

            return Results.Ok(BuildCandlesReport(r1, r2, effectivePauseMs));
        }

        private static async Task<IResult> PollCandlesTodayFuturesAsync(
            string ticker,
            MoexRealtimeRestClient client,
            int? pauseMs,
            CancellationToken ct)
        {
            int effectivePauseMs = pauseMs ?? 3000;
            DateOnly tradeDate = DateOnly.FromDateTime(DateTime.Today);

            IResult? pauseError = ValidatePauseMs(effectivePauseMs);
            if (pauseError is not null) return pauseError;

            List<CandlesDTO> r1 = await client.GetCandlesTodayFuturesAsync(
                ticker, tradeDate, interval: 1, cancellationToken: ct);
            await Task.Delay(effectivePauseMs, ct);
            List<CandlesDTO> r2 = await client.GetCandlesTodayFuturesAsync(
                ticker, tradeDate, interval: 1, cancellationToken: ct);

            return Results.Ok(BuildCandlesReport(r1, r2, effectivePauseMs));
        }

        // ═══════════════════════════════════════════════════════════
        // Builders
        // ═══════════════════════════════════════════════════════════

        private static OrderbookPollReport BuildOrderbookReport(
            RealtimeOrderbookParseResult r1,
            RealtimeOrderbookParseResult r2,
            int pauseMs)
        {
            OrderbookSnapshot snap1 = new OrderbookSnapshot(
                RowCount: r1.Rows.Count,
                SeqNum: r1.Rows.Count > 0 ? r1.Rows[0].SeqNum : null,
                UpdateTime: r1.Rows.Count > 0 ? r1.Rows[0].UpdateTime : null,
                DataVersion: r1.DataVersion.DataVersion,
                DataVersionSeqNum: r1.DataVersion.SeqNum,
                BestBid: FindBestBid(r1.Rows),
                BestAsk: FindBestAsk(r1.Rows));

            OrderbookSnapshot snap2 = new OrderbookSnapshot(
                RowCount: r2.Rows.Count,
                SeqNum: r2.Rows.Count > 0 ? r2.Rows[0].SeqNum : null,
                UpdateTime: r2.Rows.Count > 0 ? r2.Rows[0].UpdateTime : null,
                DataVersion: r2.DataVersion.DataVersion,
                DataVersionSeqNum: r2.DataVersion.SeqNum,
                BestBid: FindBestBid(r2.Rows),
                BestAsk: FindBestAsk(r2.Rows));

            return new OrderbookPollReport(
                PauseMs: pauseMs,
                Request1: snap1,
                Request2: snap2,
                SeqNumChanged: snap1.SeqNum != snap2.SeqNum,
                UpdateTimeChanged: snap1.UpdateTime != snap2.UpdateTime,
                DataVersionChanged: snap1.DataVersion != snap2.DataVersion
                    || snap1.DataVersionSeqNum != snap2.DataVersionSeqNum,
                RowCountChanged: snap1.RowCount != snap2.RowCount);
        }

        private static TradesSnapshot BuildTradesSnapshot(
            int rowCount,
            RealtimeDataVersionDTO dv,
            long? firstTradeNo, long? lastTradeNo,
            long? firstRecNo, long? lastRecNo)
        {
            return new TradesSnapshot(
                RowCount: rowCount,
                FirstTradeNo: firstTradeNo,
                LastTradeNo: lastTradeNo,
                FirstRecNo: firstRecNo,
                LastRecNo: lastRecNo,
                DataVersion: dv.DataVersion,
                DataVersionSeqNum: dv.SeqNum);
        }

        private static CandlesPollReport BuildCandlesReport(
            List<CandlesDTO> r1,
            List<CandlesDTO> r2,
            int pauseMs)
        {
            CandlesSnapshot snap1 = new CandlesSnapshot(
                RowCount: r1.Count,
                LastBegin: r1.Count > 0 ? r1[^1].Begin?.ToString("HH:mm:ss") : null,
                LastOpen: r1.Count > 0 ? r1[^1].Open : null,
                LastClose: r1.Count > 0 ? r1[^1].Close : null,
                LastHigh: r1.Count > 0 ? r1[^1].High : null,
                LastLow: r1.Count > 0 ? r1[^1].Low : null,
                LastVolume: r1.Count > 0 ? r1[^1].Volume : null);

            CandlesSnapshot snap2 = new CandlesSnapshot(
                RowCount: r2.Count,
                LastBegin: r2.Count > 0 ? r2[^1].Begin?.ToString("HH:mm:ss") : null,
                LastOpen: r2.Count > 0 ? r2[^1].Open : null,
                LastClose: r2.Count > 0 ? r2[^1].Close : null,
                LastHigh: r2.Count > 0 ? r2[^1].High : null,
                LastLow: r2.Count > 0 ? r2[^1].Low : null,
                LastVolume: r2.Count > 0 ? r2[^1].Volume : null);

            bool lastCandleChanged =
                snap1.LastClose != snap2.LastClose
                || snap1.LastHigh != snap2.LastHigh
                || snap1.LastLow != snap2.LastLow
                || snap1.LastVolume != snap2.LastVolume;

            return new CandlesPollReport(
                PauseMs: pauseMs,
                Request1: snap1,
                Request2: snap2,
                LastCandleChanged: lastCandleChanged,
                RowCountChanged: snap1.RowCount != snap2.RowCount);
        }

        // ═══════════════════════════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════════════════════════

        private static double? FindBestBid(List<RealtimeOrderbookRowDTO> rows)
        {
            double? best = null;

            for (int i = 0; i < rows.Count; i++)
            {
                RealtimeOrderbookRowDTO row = rows[i];

                if (row.BuySell != "B")
                    continue;

                if (!row.Price.HasValue)
                    continue;

                if (!best.HasValue || row.Price.Value > best.Value)
                    best = row.Price.Value;
            }

            return best;
        }

        private static double? FindBestAsk(List<RealtimeOrderbookRowDTO> rows)
        {
            double? best = null;

            for (int i = 0; i < rows.Count; i++)
            {
                RealtimeOrderbookRowDTO row = rows[i];

                if (row.BuySell != "S")
                    continue;

                if (!row.Price.HasValue)
                    continue;

                if (!best.HasValue || row.Price.Value < best.Value)
                    best = row.Price.Value;
            }

            return best;
        }

        private static int CountDuplicates(List<long> values, HashSet<long> reference)
        {
            int count = 0;

            for (int i = 0; i < values.Count; i++)
            {
                if (reference.Contains(values[i]))
                    count++;
            }

            return count;
        }

        private static IResult? ValidatePauseMs(int pauseMs)
        {
            if (pauseMs < 0 || pauseMs > 60_000)
                return Results.BadRequest("pauseMs must be between 0 and 60000.");

            return null;
        }

        private static bool IsAscending(List<long> values)
        {
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] < values[i - 1])
                    return false;
            }
            return true;
        }
        }
    }
