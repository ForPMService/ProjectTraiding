using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Contracts;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Management.Endpoints
{
    public static class CalendarEndpoints
    {
        public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/calendar/days", async (
                CalendarLoadRequest request,
                MoexCalendarLoader loader,
                CancellationToken ct) =>
            {
                DateOnly dateFrom = request.DateFrom ?? MoexCalendarLoader.GetDefaultDateFrom();
                if (request.DateTill is DateOnly dateTill && dateFrom > dateTill)
                    return Results.BadRequest("dateFrom не может быть позже dateTill");
                return CalendarResponse(await loader.LoadDaysAsync(dateFrom, request.DateTill, ct));
            });

            routes.MapPost("/management/calendar/intervals", async (
                MoexCalendarLoader loader,
                CancellationToken ct) => CalendarResponse(await loader.LoadIntervalsAsync(ct)));

            routes.MapPost("/management/calendar/expirations", async (
                CalendarLoadRequest request,
                MoexCalendarLoader loader,
                CancellationToken ct) =>
            {
                DateOnly dateFrom = request.DateFrom ?? MoexCalendarLoader.GetDefaultDateFrom();
                if (request.DateTill is DateOnly dateTill && dateFrom > dateTill)
                    return Results.BadRequest("dateFrom не может быть позже dateTill");
                return CalendarResponse(await loader.LoadExpirationsAsync(dateFrom, request.DateTill, ct));
            });

            routes.MapPost("/management/calendar/splits", async (
                MoexCalendarLoader loader,
                CancellationToken ct) => CalendarResponse(await loader.LoadSplitsAsync(ct)));

            routes.MapPost("/management/calendar/days/override", async (
                CalendarDayOverrideRequest request,
                MoexCalendarWriter writer,
                CancellationToken ct) =>
            {
                string? validationError = ValidateOverride(request);
                if (validationError is not null)
                    return Results.BadRequest(validationError);
                int rowsWritten = await writer.OverrideDayAsync(
                    request.Market!, request.Date!.Value, request.IsTraded!.Value, request.Note, ct);
                if (rowsWritten == 0)
                    return Results.NotFound("Календарь для указанной даты ещё не загружен.");
                return CalendarResponse(rowsWritten);
            });

            return routes;
        }

        private static IResult CalendarResponse(int rowsWritten) => Results.Json(
            new CalendarOperationResponse(rowsWritten),
            ManagementJsonContext.Default.CalendarOperationResponse);

        private static string? ValidateOverride(CalendarDayOverrideRequest request)
        {
            if (!MoexDomainRules.IsMarket(request.Market))
                return "market должен быть stock или futures";
            if (request.Date is null)
                return "date обязателен";
            if (!MoexDomainRules.IsCalendarTradingFlag(request.IsTraded))
                return "isTraded должен быть 0 или 1";
            return null;
        }
    }
}
