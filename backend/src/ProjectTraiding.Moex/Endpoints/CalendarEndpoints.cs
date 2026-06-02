using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Serialization;
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
            /* _Old: endpoint offdays-all отключён, сценарий перенесён в Old
            routes.MapGet("/calendar/offdays-all", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetOffDaysAll", MoexLogSources.Calendar, "/calendars.json", string.Empty);
                return Results.Json(
                    await c.GetOffDaysAll(cancellationToken: ct),
                    AppJsonContext.Default.ListCalendarOffDaysAllDTO);
            });
            */

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

            /* _Old: старые calendar endpoint-ы отключены после переноса сценариев в Old
            routes.MapGet("/calendar/stock-session", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetStockSessionWithTypes", MoexLogSources.Calendar, "/calendars/stock/session.json", string.Empty);
                var (sessions, _) = await c.GetStockSessionWithTypes(cancellationToken: ct);
                return Results.Json(sessions, AppJsonContext.Default.ListCalendarStockSessionDTO);
            });

            routes.MapGet("/calendar/stock-session-types", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetStockSessionTypes", MoexLogSources.Calendar, "/calendars/stock/session.json", string.Empty);
                var (_, types) = await c.GetStockSessionWithTypes(cancellationToken: ct);
                return Results.Json(types, AppJsonContext.Default.ListCalendarSessionTypeDTO);
            });

            routes.MapGet("/calendar/futures-session", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetFuturesSessionWithTypes", MoexLogSources.Calendar, "/calendars/futures/session.json", string.Empty);
                var (sessions, _) = await c.GetFuturesSessionWithTypes(cancellationToken: ct);
                return Results.Json(sessions, AppJsonContext.Default.ListCalendarFuturesSessionDTO);
            });

            routes.MapGet("/calendar/futures-session-types", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetFuturesSessionTypes", MoexLogSources.Calendar, "/calendars/futures/session.json", string.Empty);
                var (_, types) = await c.GetFuturesSessionWithTypes(cancellationToken: ct);
                return Results.Json(types, AppJsonContext.Default.ListCalendarSessionTypeDTO);
            });

            routes.MapGet("/calendar/forts-contracts", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetFortsContracts", MoexLogSources.Calendar, "/calendars/futures/securities.json", string.Empty);
                var (forts, _) = await c.GetFuturesSecuritiesAll(cancellationToken: ct);
                return Results.Json(forts, AppJsonContext.Default.ListCalendarFortsContractDTO);
            });

            routes.MapGet("/calendar/options-series", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetOptionsSeries", MoexLogSources.Calendar, "/calendars/futures/securities.json", string.Empty);
                var (_, options) = await c.GetFuturesSecuritiesAll(cancellationToken: ct);
                return Results.Json(options, AppJsonContext.Default.ListCalendarOptionsSeriesDTO);
            });

            routes.MapGet("/calendar/suspended-reasons", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSuspendedReasons", MoexLogSources.Calendar, "/calendars/stock/securities/suspended/details.json", string.Empty);
                return Results.Json(await c.GetSuspendedReasons(cancellationToken: ct), AppJsonContext.Default.ListCalendarSuspendedReasonDTO);
            });

            routes.MapGet("/calendar/suspended", (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSuspended", MoexLogSources.Calendar, "/calendars/stock/securities/suspended/details.json", string.Empty);
                return StreamSuspended(c, ct);
            });

            routes.MapGet("/calendar/security-attributes", async (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSecurityAttributes", MoexLogSources.Calendar, "/calendars/stock/securities/changes.json", string.Empty);
                return Results.Json(await c.GetSecurityAttributes(cancellationToken: ct), AppJsonContext.Default.ListCalendarSecurityAttributeDTO);
            });

            routes.MapGet("/calendar/security-changes", (
                MoexHttpCalendarClient c,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("CalendarEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSecurityChanges", MoexLogSources.Calendar, "/calendars/stock/securities/changes.json", string.Empty);
                return StreamSecurityChanges(c, ct);
            });
            */

            return routes;
        }

        /* _Old: streaming helper-методы для старых calendar endpoint-ов перенесены в Old
        static async IAsyncEnumerable<CalendarSuspendedDTO> StreamSuspended(
            MoexHttpCalendarClient client,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<CalendarSuspendedDTO> batch in client.GetSuspended(cancellationToken: ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<CalendarSecurityChangeDTO> StreamSecurityChanges(
            MoexHttpCalendarClient client,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<CalendarSecurityChangeDTO> batch in client.GetSecurityChanges(cancellationToken: ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }
        */
    }
}
