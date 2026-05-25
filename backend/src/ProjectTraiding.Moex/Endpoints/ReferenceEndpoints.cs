using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Contracts.Serialization;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Source endpoint-ы MOEX: в момент запроса идут в MOEX,
    /// парсят ответ и возвращают DTO MOEX.
    /// Это не ручки витрины для фронта; ручки витрины появятся позже
    /// и будут читать данные из PostgreSQL/ClickHouse.
    /// </summary>
    public static class ReferenceEndpoints
    {
        public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/GetStockMarkets", async (
                MoexHttpIssClient moexHttpIssClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) => {
                    string url = "/engines/stock/markets/shares/boards/tqbr/securities.json";
                    var logger = loggerFactory.CreateLogger("ReferenceEndpoints");
                    MoexLogMessages.LoadStarted(logger, "GetStockMarkets", MoexLogSources.Iss, url, string.Empty);
                    List<StockSecurityDTO> response = await moexHttpIssClient.GetInfoTradedStockAssets(
                        url,
                        cancellationToken: ct);
                    return Results.Json(response, AppJsonContext.Default.ListStockSecurityDTO);
                });

            routes.MapGet("/GetFuturesMarkets", async (
                MoexHttpIssClient moexHttpIssClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) => {
                    string url = "/engines/futures/markets/forts/boards/RFUD/securities.json";
                    var logger = loggerFactory.CreateLogger("ReferenceEndpoints");
                    MoexLogMessages.LoadStarted(logger, "GetFuturesMarkets", MoexLogSources.Iss, url, string.Empty);
                    List<FuturesSecurityDTO> response = await moexHttpIssClient.GetInfoTradedFuturesAssets(
                        url,
                        cancellationToken: ct);
                    return Results.Json(response, AppJsonContext.Default.ListFuturesSecurityDTO);
                });

            return routes;
        }
    }
}
