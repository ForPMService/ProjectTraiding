using ProjectTraiding.Moex.Clients;
using Microsoft.AspNetCore.Mvc;

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
                long? tradeno,
                int? reversed,
                int? start,
                int? latest,
                [FromQuery(Name = "next_trade")] int? nextTrade,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateTradeParameters(tradeno, null, reversed, start, latest, nextTrade);
                if (validationMessage is not null)
                    return DiagnosticResponses.CreateDiagnosticError(400, "MOEX_REALTIME", "trades", "stock", validationMessage, ticker);

                string url = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/trades.json";
                Dictionary<string, string> queryParams = CreateTradeQuery(tradeno, null, reversed, start, latest, nextTrade);
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
                return Results.Text(raw, "application/json");
            });

            group.MapGet("/raw/trades-futures/{ticker}", async (
                string ticker,
                long? tradeno,
                long? recno,
                int? reversed,
                int? start,
                int? latest,
                [FromQuery(Name = "next_trade")] int? nextTrade,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateTradeParameters(tradeno, recno, reversed, start, latest, nextTrade);
                if (validationMessage is not null)
                    return DiagnosticResponses.CreateDiagnosticError(400, "MOEX_REALTIME", "trades", "futures", validationMessage, ticker);

                string url = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/trades.json";
                Dictionary<string, string> queryParams = CreateTradeQuery(tradeno, recno, reversed, start, latest, nextTrade);
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
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

        private static Dictionary<string, string> CreateTradeQuery(long? tradeno, long? recno, int? reversed, int? start, int? latest, int? nextTrade)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>();
            if (tradeno is not null)
                queryParams["tradeno"] = tradeno.Value.ToString();
            if (recno is not null)
                queryParams["recno"] = recno.Value.ToString();
            if (reversed is not null)
                queryParams["reversed"] = reversed.Value.ToString();
            if (start is not null)
                queryParams["start"] = start.Value.ToString();
            if (latest is not null)
                queryParams["latest"] = latest.Value.ToString();
            if (nextTrade is not null)
                queryParams["next_trade"] = nextTrade.Value.ToString();

            return queryParams;
        }

        private static string? ValidateTradeParameters(long? tradeno, long? recno, int? reversed, int? start, int? latest, int? nextTrade)
        {
            if (tradeno is not null && recno is not null)
                return "Query parameters tradeno and recno cannot be used together.";
            if (tradeno is < 0 || recno is < 0)
                return "Trade number parameters must be non-negative integers.";
            if (start is < 0)
                return "Query parameter start must be a non-negative integer.";
            if (latest is not null && latest is not 0 and not 1)
                return "Query parameter latest must be 0 or 1.";
            if (nextTrade is not null && nextTrade is not 0 and not 1)
                return "Query parameter next_trade must be 0 or 1.";

            return null;
        }
    }
}
