using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Contracts.Dto.Operations;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.Redis;

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
                LoadResultDto result = await LoadStockInstrumentsAsync(client, writer, ct);
                return Results.Json(result, AppJsonContext.Default.LoadResultDto);
            });

            routes.MapGet("/operations/moex/sync/instruments/futures", async (
                HttpContext httpContext,
                MoexHttpAlgClient client,
                MoexInstrumentWriter writer,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                LoadResultDto result = await LoadFuturesInstrumentsAsync(client, writer, ct);
                return Results.Json(result, AppJsonContext.Default.LoadResultDto);
            });

            routes.MapGet("/operations/moex/sync/bootstrap", async (
                HttpContext httpContext,
                MoexHttpIssClient issClient,
                MoexHttpAlgClient algClient,
                MoexHttpCalendarClient calendarClient,
                MoexInstrumentWriter instrumentWriter,
                CatalogEventPublisher catalogEventPublisher,
                MoexCalendarWriter calendarWriter,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";

                LoadResultDto[] results = new LoadResultDto[]
                {
                    await LoadStockInstrumentsAsync(issClient, instrumentWriter, ct),
                    await LoadFuturesInstrumentsAsync(algClient, instrumentWriter, ct),
                    await CalendarEndpoints.LoadStockCalendarAsync(calendarClient, calendarWriter, ct),
                    await CalendarEndpoints.LoadFuturesCalendarAsync(calendarClient, calendarWriter, ct)
                };
                // Справочник успешно обновлён в базе истины — извещаем витрину одним событием.
                // Стоит после загрузок: если любая из них бросит исключение, сюда не дойдём и событие не выйдет.
                await catalogEventPublisher.PublishChangedAsync();

                return Results.Json(results, AppJsonContext.Default.LoadResultDtoArray);
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
