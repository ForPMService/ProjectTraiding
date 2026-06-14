using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Storage.Postgres;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Карточки инструментов — списочные ответы с securities + marketdata.
    ///
    /// Акции:    ISS (публичный), парсинг ParsingInstrumentCardUtf8.ParseStockCards.
    /// Фьючерсы: APIM (платный), парсинг ParsingInstrumentCardUtf8.ParseFuturesCards.
    ///
    /// Это source endpoint-ы: в момент запроса идут в MOEX, парсят и возвращают DTO.
    /// Ручки витрины для фронта появятся позже.
    /// </summary>
    public static class InstrumentCardEndpoints
    {

        public static IEndpointRouteBuilder MapInstrumentCardLoadEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/loads/instrument-cards/stock", async (
                MoexHttpIssClient client,
                MoexInstrumentWriter writer,
                CancellationToken ct) =>
            {
                var cards = await client.GetStockInstrumentCards(ct);
                await writer.UpsertStocksAsync(cards, ct);
                return Results.NoContent();
            });

            routes.MapGet("/loads/instrument-cards/futures", async (
                MoexHttpAlgClient client,
                MoexInstrumentWriter writer,
                CancellationToken ct) =>
            {
                var cards = await client.GetFuturesInstrumentCards(ct);
                await writer.UpsertFuturesAsync(cards, ct);
                return Results.NoContent();
            });

            return routes;
        }
    }
}
