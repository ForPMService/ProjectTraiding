using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Storage.Postgres;
using System.Runtime.CompilerServices;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Календарные endpoint-ы MOEX.
    /// Source/parsed routes возвращают DTO без записи, sync routes синхронизируют данные в PostgreSQL.
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
            routes.MapGet("/operations/moex/sync/calendar/stock", async (
                HttpContext httpContext,
                MoexHttpCalendarClient client,
                MoexCalendarWriter writer,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                List<CalendarOffDaysMarketDTO> days = await client.GetStockOffDays(cancellationToken: ct);
                await writer.UpsertStockOffDaysAsync(days, ct);
                return Results.NoContent();
            });

            routes.MapGet("/operations/moex/sync/calendar/futures", async (
                HttpContext httpContext,
                MoexHttpCalendarClient client,
                MoexCalendarWriter writer,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                List<CalendarOffDaysMarketDTO> days = await client.GetFuturesOffDays(cancellationToken: ct);
                await writer.UpsertFuturesOffDaysAsync(days, ct);
                return Results.NoContent();
            });

            return routes;
        }
    }
}
