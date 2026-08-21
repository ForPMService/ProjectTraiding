using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
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
                MoexHttpIssClient client,
                MoexInstrumentWriter writer,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                LoadResultDto result = await LoadStockInstrumentsAsync(client, writer, ct);
                return Results.Json(result, ManagementJsonContext.Default.LoadResultDto);
            });

            routes.MapPost("/management/sync/instruments/futures", async (
                HttpContext httpContext,
                MoexHttpAlgClient client,
                MoexInstrumentWriter writer,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                LoadResultDto result = await LoadFuturesInstrumentsAsync(client, writer, ct);
                return Results.Json(result, ManagementJsonContext.Default.LoadResultDto);
            });

            routes.MapPost("/management/sync/bootstrap", async (
                HttpContext httpContext,
                MoexHttpIssClient issClient,
                MoexHttpAlgClient algClient,
                MoexInstrumentWriter instrumentWriter,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";

                LoadResultDto[] results =
                [
                    await LoadStockInstrumentsAsync(issClient, instrumentWriter, ct),
                    await LoadFuturesInstrumentsAsync(algClient, instrumentWriter, ct)
                ];
                return Results.Json(results, ManagementJsonContext.Default.LoadResultDtoArray);
            });

            return routes;
        }

        private static async Task<LoadResultDto> LoadStockInstrumentsAsync(
            MoexHttpIssClient client,
            MoexInstrumentWriter writer,
            CancellationToken ct)
        {
            List<StockInstrumentCardDTO> cards = await client.GetStockInstrumentCards(ct);
            DbWriteResult result = await writer.UpsertStocksAsync(cards, ct);
            return new LoadResultDto(
                Operation: "sync_instruments_stock",
                Source: "MOEX_ISS",
                Target: "moex_instruments/moex_stock_details",
                Status: "ok",
                InputCount: result.InputCount,
                RowsWritten: result.RowsWritten,
                ElapsedMs: result.Elapsed.TotalMilliseconds);
        }

        private static async Task<LoadResultDto> LoadFuturesInstrumentsAsync(
            MoexHttpAlgClient client,
            MoexInstrumentWriter writer,
            CancellationToken ct)
        {
            List<FuturesInstrumentCardDTO> cards = await client.GetFuturesInstrumentCards(ct);
            DbWriteResult result = await writer.UpsertFuturesAsync(cards, ct);
            return new LoadResultDto(
                Operation: "sync_instruments_futures",
                Source: "MOEX_ALGOPACK",
                Target: "moex_instruments/moex_futures_details",
                Status: "ok",
                InputCount: result.InputCount,
                RowsWritten: result.RowsWritten,
                ElapsedMs: result.Elapsed.TotalMilliseconds);
        }
    }
}
