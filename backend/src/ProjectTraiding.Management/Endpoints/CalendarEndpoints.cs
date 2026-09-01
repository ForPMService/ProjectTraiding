using ProjectTraiding.CustomFeatures.Loading;
using ProjectTraiding.CustomFeatures.StorageBase.Postgres;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Contracts;

namespace ProjectTraiding.Management.Endpoints
{
    public static class CalendarEndpoints
    {
        public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/calendar/days", async (
                CalendarLoadRequest request,
                CalendarLoader loader,
                CancellationToken ct) =>
            {
                DateOnly dateFrom = request.DateFrom ?? CalendarLoader.GetDefaultDateFrom();
                if (request.DateTill is DateOnly dateTill && dateFrom > dateTill)
                    return Results.BadRequest("dateFrom не может быть позже dateTill");
                return CalendarResponse(await loader.LoadDaysAsync(dateFrom, request.DateTill, ct));
            });

            routes.MapPost("/management/calendar/intervals", async (
                CalendarLoader loader,
                CancellationToken ct) => CalendarResponse(await loader.LoadIntervalsAsync(ct)));

            routes.MapPost("/management/calendar/expirations", async (
                CalendarLoadRequest request,
                CalendarLoader loader,
                CancellationToken ct) =>
            {
                DateOnly dateFrom = request.DateFrom ?? CalendarLoader.GetDefaultDateFrom();
                if (request.DateTill is DateOnly dateTill && dateFrom > dateTill)
                    return Results.BadRequest("dateFrom не может быть позже dateTill");
                return CalendarResponse(await loader.LoadExpirationsAsync(dateFrom, request.DateTill, ct));
            });

            routes.MapPost("/management/calendar/splits", async (
                CalendarLoader loader,
                CancellationToken ct) => CalendarResponse(await loader.LoadSplitsAsync(ct)));

            routes.MapPost("/management/calendar/days/override", async (
                CalendarDayOverrideRequest request,
                CalendarDayWriter writer,
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
            if (request.IsTraded is not (0 or 1))
                return "isTraded должен быть 0 или 1";
            return null;
        }
    }
}
