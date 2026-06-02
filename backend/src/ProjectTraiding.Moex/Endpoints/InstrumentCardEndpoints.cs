using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Serialization;

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
        public static IEndpointRouteBuilder MapInstrumentCardEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/instrument-cards/stock", async (
                MoexHttpIssClient client,
                CancellationToken ct) =>
            {
                var cards = await client.GetStockInstrumentCards(ct);
                return Results.Json(cards, AppJsonContext.Default.ListStockInstrumentCardDTO);
            });

            routes.MapGet("/instrument-cards/futures", async (
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                var cards = await client.GetFuturesInstrumentCards(ct);
                return Results.Json(cards, AppJsonContext.Default.ListFuturesInstrumentCardDTO);
            });

            return routes;
        }
    }
}
