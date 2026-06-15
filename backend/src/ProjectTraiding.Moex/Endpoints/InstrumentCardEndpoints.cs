using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Операционные sync endpoint-ы для карточек инструментов.
    /// По запросу идут в MOEX, парсят ответ и синхронизируют данные в PostgreSQL.
    /// </summary>
    public static class InstrumentCardEndpoints
    {

        public static IEndpointRouteBuilder MapInstrumentCardLoadEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/operations/moex/sync/instruments/stock", async (
                HttpContext httpContext,
                MoexHttpIssClient client,
                MoexInstrumentWriter writer,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                List<StockInstrumentCardDTO> cards = await client.GetStockInstrumentCards(ct);
                await writer.UpsertStocksAsync(cards, ct);
                return Results.NoContent();
            });

            routes.MapGet("/operations/moex/sync/instruments/futures", async (
                HttpContext httpContext,
                MoexHttpAlgClient client,
                MoexInstrumentWriter writer,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                List<FuturesInstrumentCardDTO> cards = await client.GetFuturesInstrumentCards(ct);
                await writer.UpsertFuturesAsync(cards, ct);
                return Results.NoContent();
            });

            return routes;
        }
    }
}
