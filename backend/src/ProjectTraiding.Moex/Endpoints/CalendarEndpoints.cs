using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Storage.Postgres;
using System.Runtime.CompilerServices;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Source endpoint-ы MOEX: в момент запроса идут в MOEX,
    /// парсят ответ и возвращают DTO MOEX.
    /// Это не ручки витрины для фронта; ручки витрины появятся позже
    /// и будут читать данные из PostgreSQL/ClickHouse.
    /// </summary>
    public static class CalendarEndpoints
    {
        public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder routes)
        {
            // === ISS Календарь — Обзор ===

            routes.MapGet("/calendar/stock-offdays", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetStockOffDays", MoexLogSources.Calendar, "/calendars/stock.json", string.Empty);
                return Results.Json(
                    await c.GetStockOffDays(cancellationToken: ct),
                    AppJsonContext.Default.ListCalendarOffDaysMarketDTO);
            });

            routes.MapGet("/calendar/futures-offdays", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetFuturesOffDays", MoexLogSources.Calendar, "/calendars/futures.json", string.Empty);
                return Results.Json(
                    await c.GetFuturesOffDays(cancellationToken: ct),
                    AppJsonContext.Default.ListCalendarOffDaysMarketDTO);
            });

            return routes;
        }
        public static IEndpointRouteBuilder MapCalendarLoadEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/loads/calendar/stock", async (
                MoexHttpCalendarClient client,
                MoexCalendarWriter writer,
                CancellationToken ct) =>
            {
                var days = await client.GetStockOffDays(cancellationToken: ct);
                await writer.UpsertStockOffDaysAsync(days, ct);
                return Results.NoContent();
            });

            routes.MapGet("/loads/calendar/futures", async (
                MoexHttpCalendarClient client,
                MoexCalendarWriter writer,
                CancellationToken ct) =>
            {
                var days = await client.GetFuturesOffDays(cancellationToken: ct);
                await writer.UpsertFuturesOffDaysAsync(days, ct);
                return Results.NoContent();
            });

            return routes;
        }
    }
}
