using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Management.Endpoints
{
    /// <summary>
    /// Команды синхронизации справочника инструментов с Московской биржей.
    /// </summary>
    public static class InstrumentCardEndpoints
    {
        public static IEndpointRouteBuilder MapInstrumentCardLoadEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/sync/instruments/stock", async (
                HttpContext httpContext,
                MoexInstrumentSyncLoader loader,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                DbWriteResult result = await loader.LoadStockAsync(ct);
                return Results.Json(
                    BuildResult("sync_instruments_stock", "MOEX_ISS",
                        MoexInstrumentSyncLoader.StockTarget, result),
                    ManagementJsonContext.Default.LoadResultDto);
            });

            routes.MapPost("/management/sync/instruments/futures", async (
                HttpContext httpContext,
                MoexInstrumentSyncLoader loader,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                DbWriteResult result = await loader.LoadFuturesAsync(ct);
                return Results.Json(
                    BuildResult("sync_instruments_futures", "MOEX_ALGOPACK",
                        MoexInstrumentSyncLoader.FuturesTarget, result),
                    ManagementJsonContext.Default.LoadResultDto);
            });

            routes.MapPost("/management/sync/bootstrap", async (
                HttpContext httpContext,
                MoexInstrumentSyncLoader loader,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";

                DbWriteResult stock = await loader.LoadStockAsync(ct);
                DbWriteResult futures = await loader.LoadFuturesAsync(ct);
                LoadResultDto[] results =
                [
                    BuildResult("sync_instruments_stock", "MOEX_ISS",
                        MoexInstrumentSyncLoader.StockTarget, stock),
                    BuildResult("sync_instruments_futures", "MOEX_ALGOPACK",
                        MoexInstrumentSyncLoader.FuturesTarget, futures)
                ];
                return Results.Json(results, ManagementJsonContext.Default.LoadResultDtoArray);
            });

            return routes;
        }

        private static LoadResultDto BuildResult(
            string operation,
            string source,
            string target,
            DbWriteResult result)
        {
            return new LoadResultDto(
                Operation: operation,
                Source: source,
                Target: target,
                Status: "ok",
                InputCount: result.InputCount,
                RowsWritten: result.RowsWritten,
                ElapsedMs: result.Elapsed.TotalMilliseconds);
        }
    }
}
