using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;

namespace ProjectTraiding.Diagnostics.Endpoints
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
            var group = routes.MapGroup("/realtime");

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
                // Весь московский торговый день целиком: диагностика должна видеть все страницы,
                // включая утреннюю и вечернюю сессии.
                DateOnly tradeDate = MoexTime.Today;
                DateTime from = tradeDate.ToDateTime(TimeOnly.MinValue);
                DateTime till = tradeDate.ToDateTime(new TimeOnly(23, 59, 59));

                var result = await client.GetCandlesTodayStockAsync(
                    ticker,
                    from,
                    till,
                    interval: 1,
                    cancellationToken: ct);
                return Results.Ok(result);
            });

            group.MapGet("/candles-today-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                // Весь московский торговый день целиком: диагностика должна видеть все страницы,
                // включая утреннюю и вечернюю сессии.
                DateOnly tradeDate = MoexTime.Today;
                DateTime from = tradeDate.ToDateTime(TimeOnly.MinValue);
                DateTime till = tradeDate.ToDateTime(new TimeOnly(23, 59, 59));

                var result = await client.GetCandlesTodayFuturesAsync(
                    ticker,
                    from,
                    till,
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
