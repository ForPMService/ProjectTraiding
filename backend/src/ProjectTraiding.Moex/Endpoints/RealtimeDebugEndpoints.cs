using ProjectTraiding.Moex.Clients;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Диагностические endpoint-ы для real-time REST MOEX.
    /// 
    /// Не будущий API — только для проверки source contract:
    ///   raw → что реально отдал MOEX;
    ///   parsed → что наш client/parser это корректно понял.
    /// 
    /// Используются при diagnostic REST poll cycle (Шаг 9).
    /// </summary>
    public static class RealtimeDebugEndpoints
    {
        public static IEndpointRouteBuilder MapRealtimeDebugEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/debug/realtime");

            // ═══════════════════════════════════════════════════════════
            // Parsed endpoints
            // ═══════════════════════════════════════════════════════════

            group.MapGet("/orderbook-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                var result = await client.GetOrderbookStockAsync(ticker, ct);
                return Results.Ok(result);
            });

            group.MapGet("/orderbook-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                var result = await client.GetOrderbookFuturesAsync(ticker, ct);
                return Results.Ok(result);
            });

            group.MapGet("/trades-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                var result = await client.GetTradesStockAsync(ticker, cancellationToken: ct);
                return Results.Ok(result);
            });

            group.MapGet("/trades-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                var result = await client.GetTradesFuturesAsync(ticker, cancellationToken: ct);
                return Results.Ok(result);
            });

            // ── Candles Today ──

            group.MapGet("/candles-today-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                DateOnly tradeDate = DateOnly.FromDateTime(DateTime.Today);
                var result = await client.GetCandlesTodayStockAsync(
                    ticker,
                    tradeDate,
                    interval: 1,
                    cancellationToken: ct);
                return Results.Ok(result);
            });

            group.MapGet("/candles-today-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                DateOnly tradeDate = DateOnly.FromDateTime(DateTime.Today);
                var result = await client.GetCandlesTodayFuturesAsync(
                    ticker,
                    tradeDate,
                    interval: 1,
                    cancellationToken: ct);
                return Results.Ok(result);
            });

            // ── Raw endpoints ──

            group.MapGet("/raw/orderbook-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/orderbook.json";
                string raw = await client.GetRawSectionAsync(url, cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            group.MapGet("/raw/orderbook-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/orderbook.json";
                string raw = await client.GetRawSectionAsync(url, cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            group.MapGet("/raw/trades-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/trades.json";
                string raw = await client.GetRawSectionAsync(url, cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            group.MapGet("/raw/trades-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/trades.json";
                string raw = await client.GetRawSectionAsync(url, cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            // ── MarketStatistics: Parsed ──

            group.MapGet("/market-statistics-securities-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                var result = await client.GetMarketStatisticsStockSecuritiesAsync(
                    ticker,
                    cancellationToken: ct);
                return Results.Ok(result);
            });

            group.MapGet("/market-statistics-securities-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                var result = await client.GetMarketStatisticsFuturesSecuritiesAsync(
                    ticker,
                    cancellationToken: ct);
                return Results.Ok(result);
            });

            // ── MarketStatistics: Raw ──

            group.MapGet("/raw/market-statistics-securities-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json";
                var queryParams = new Dictionary<string, string>
                {
                    ["iss.only"] = "securities",
                    ["iss.meta"] = "off",
                };
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
                return Results.Text(raw, "application/json");
            });

            group.MapGet("/raw/market-statistics-securities-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}.json";
                var queryParams = new Dictionary<string, string>
                {
                    ["iss.only"] = "securities",
                    ["iss.meta"] = "off",
                };
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
                return Results.Text(raw, "application/json");
            });
            return routes;
        }
    }
}
