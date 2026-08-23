using ProjectTraiding.Moex.Clients;

namespace ProjectTraiding.Diagnostics.Endpoints
{
    /// <summary>
    /// Диагностические endpoint-ы для сырых real-time REST-ответов MOEX.
    /// 
    /// Не будущий API — только для проверки source contract: возвращают без разбора
    /// то, что реально отдал MOEX.
    /// </summary>
    public static class RealtimeDebugEndpoints
    {
        public static IEndpointRouteBuilder MapRealtimeDebugEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/realtime");
            RouteGroupBuilder raw = routes.MapGroup("/raw");

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

            raw.MapGet("/market-statistics-marketdata-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["iss.only"] = "marketdata",
                    ["iss.meta"] = "off",
                };
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
                return Results.Text(raw, "application/json");
            });

            raw.MapGet("/market-statistics-marketdata-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["iss.only"] = "marketdata",
                    ["iss.meta"] = "off",
                };
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
                return Results.Text(raw, "application/json");
            });
            return routes;
        }
    }
}
