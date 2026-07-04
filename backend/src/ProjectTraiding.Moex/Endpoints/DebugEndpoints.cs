using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.RawCapture;
using ProjectTraiding.Moex.Options;
using System.Text.Json;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Диагностические точки исходного ответа рыночной статистики MOEX.
    /// Остаются постоянно: ручная сверка контракта источника, не будущий API витрины.
    /// </summary>
    public static class DebugEndpoints
    {
        public static IEndpointRouteBuilder MapDiagnosticDebugEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/raw/market-statistics-marketdata-stock/{ticker}", async (
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

            routes.MapGet("/raw/market-statistics-marketdata-futures/{ticker}", async (
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
