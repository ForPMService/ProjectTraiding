using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Management.Endpoints
{
    public static class CalendarEndpoints
    {
        private const string DeferredAlgorithmMessage = "алгоритм выносится в отдельное задание";

        public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/calendar/periods/snapshot", async (
                MoexCalendarLoader loader,
                CancellationToken ct) =>
            {
                int rowsWritten = await loader.SnapshotPeriodsAsync(ct);
                return CalendarResponse(rowsWritten);
            });

            routes.MapPost("/management/calendar/days", async (
                CalendarLoadRequest request,
                MoexCalendarLoader loader,
                CancellationToken ct) =>
            {
                (DateOnly dateFrom, DateOnly dateTill) = ResolveRange(request);
                if (dateFrom > dateTill)
                    return Results.BadRequest("dateFrom не может быть позже dateTill");
                int rowsWritten = await loader.LoadDaysAsync(dateFrom, dateTill, ct);
                return CalendarResponse(rowsWritten);
            });

            routes.MapPost("/management/calendar/intervals", async (
                MoexCalendarLoader loader,
                CancellationToken ct) =>
            {
                int rowsWritten = await loader.LoadIntervalsAsync(ct);
                return CalendarResponse(rowsWritten);
            });

            routes.MapPost("/management/calendar/expirations", async (
                CalendarLoadRequest request,
                MoexCalendarLoader loader,
                CancellationToken ct) =>
            {
                (DateOnly dateFrom, DateOnly dateTill) = ResolveRange(request);
                if (dateFrom > dateTill)
                    return Results.BadRequest("dateFrom не может быть позже dateTill");
                int rowsWritten = await loader.LoadExpirationsAsync(dateFrom, dateTill, ct);
                return CalendarResponse(rowsWritten);
            });

            routes.MapPost("/management/calendar/splits", async (
                MoexCalendarLoader loader,
                CancellationToken ct) =>
            {
                int rowsWritten = await loader.LoadSplitsAsync(ct);
                return CalendarResponse(rowsWritten);
            });

            routes.MapPost("/management/calendar/bounds/recalc", () =>
                Results.Text(
                    DeferredAlgorithmMessage,
                    "text/plain",
                    statusCode: StatusCodes.Status501NotImplemented));

            routes.MapPost("/management/calendar/verify", () =>
                Results.Text(
                    DeferredAlgorithmMessage,
                    "text/plain",
                    statusCode: StatusCodes.Status501NotImplemented));

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
                return CalendarResponse(rowsWritten);
            });

            routes.MapPost("/management/events", async (
                ManualEventCreateRequest request,
                MoexManualEventWriter writer,
                CancellationToken ct) =>
            {
                string? validationError = ValidateManualEvent(request);
                if (validationError is not null)
                    return Results.BadRequest(validationError);
                ManualEventWriteDTO manualEvent = new ManualEventWriteDTO
                {
                    SecId = request.SecId!,
                    EventType = request.EventType!,
                    EventDate = request.EventDate!.Value,
                    KnownFrom = request.KnownFrom!.Value,
                    RecordDate = request.RecordDate,
                    LastTradeDate = request.LastTradeDate,
                    PaymentDate = request.PaymentDate,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    SourceNote = request.SourceNote,
                };
                int rowsWritten = await writer.InsertAsync(manualEvent, ct);
                return CalendarResponse(rowsWritten);
            });

            return routes;
        }

        private static IResult CalendarResponse(int rowsWritten)
        {
            CalendarOperationResponse response = new CalendarOperationResponse(rowsWritten);
            return Results.Json(response, ManagementJsonContext.Default.CalendarOperationResponse);
        }

        private static (DateOnly DateFrom, DateOnly DateTill) ResolveRange(
            CalendarLoadRequest request)
        {
            DateOnly dateFrom = request.DateFrom ?? MoexCalendarLoader.GetDefaultDateFrom();
            DateOnly dateTill = request.DateTill
                ?? DateOnly.FromDateTime(DateTime.UtcNow + TimeSpan.FromHours(3));
            return (dateFrom, dateTill);
        }

        private static string? ValidateOverride(CalendarDayOverrideRequest request)
        {
            if (request.Market != "stock" && request.Market != "futures")
                return "market должен быть stock или futures";
            if (request.Date is null)
                return "date обязателен";
            if (request.IsTraded is not 0 and not 1)
                return "isTraded должен быть 0 или 1";
            return null;
        }

        private static string? ValidateManualEvent(ManualEventCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SecId))
                return "secid обязателен";
            if (request.EventType != "dividend"
                && request.EventType != "meeting"
                && request.EventType != "issue"
                && request.EventType != "delisting_announced")
            {
                return "eventType должен быть dividend, meeting, issue или delisting_announced";
            }
            if (request.EventDate is null)
                return "eventDate обязателен";
            if (request.KnownFrom is null)
                return "knownFrom обязателен";
            return null;
        }
    }
}
