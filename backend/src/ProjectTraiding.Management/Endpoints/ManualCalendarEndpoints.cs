using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Moex.Contracts;

namespace ProjectTraiding.Management.Endpoints
{
    public static class ManualCalendarEndpoints
    {
        public static IEndpointRouteBuilder MapManualCalendarEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/events", async (
                ManualEventCreateRequest request,
                ManualEventWriter writer,
                CancellationToken ct) =>
            {
                string? error = ValidateManualEvent(request);
                if (error is not null)
                    return Results.BadRequest(error);

                Guid id = await writer.CreateAsync(request, ct);
                return Results.Json(
                    new ManualEventCreateResponse(id, 1),
                    ManagementJsonContext.Default.ManualEventCreateResponse);
            });

            routes.MapPost("/management/calendar/periods", async (
                TradingPeriodCreateRequest request,
                TradingPeriodWriter writer,
                CancellationToken ct) =>
            {
                string? error = ValidateTradingPeriod(request);
                if (error is not null)
                    return Results.BadRequest(error);

                int rowsWritten = await writer.CreateAsync(request, ct);
                return CalendarResponse(rowsWritten);
            });

            routes.MapPost("/management/calendar/period-types", async (
                TradingPeriodTypeCreateRequest request,
                TradingPeriodTypeWriter writer,
                CancellationToken ct) =>
            {
                string? error = ValidateTradingPeriodType(request);
                if (error is not null)
                    return Results.BadRequest(error);

                int rowsWritten = await writer.CreateAsync(request, ct);
                return CalendarResponse(rowsWritten);
            });

            return routes;
        }

        private static IResult CalendarResponse(int rowsWritten)
        {
            return Results.Json(
                new CalendarOperationResponse(rowsWritten),
                ManagementJsonContext.Default.CalendarOperationResponse);
        }

        private static string? ValidateManualEvent(ManualEventCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Secid))
                return "secid обязателен";
            if (request.EventType is not ("dividend" or "meeting" or "issue" or "buyback" or "delisting_announced"))
                return "eventType должен быть dividend, meeting, issue, buyback или delisting_announced";
            if (request.EventStage is not ("announced" or "recommended" or "approved" or "completed" or "cancelled"))
                return "eventStage должен быть announced, recommended, approved, completed или cancelled";
            if (request.EventDate is null)
                return "eventDate обязателен";
            if (request.KnownFrom is null)
                return "knownFrom обязателен";
            return null;
        }

        private static string? ValidateTradingPeriod(TradingPeriodCreateRequest request)
        {
            if (!MoexDomainRules.IsMarket(request.Market))
                return "market должен быть stock или futures";
            if (request.ValidFrom is null)
                return "validFrom обязателен";
            if (request.ValidTill is null)
                return "validTill обязателен";
            if (request.ValidFrom > request.ValidTill)
                return "validFrom не может быть позже validTill";
            if (string.IsNullOrWhiteSpace(request.Boardid))
                return "boardid обязателен";
            if (string.IsNullOrWhiteSpace(request.PeriodType))
                return "periodType обязателен";
            if (request.TimeFrom is null)
                return "timeFrom обязателен";
            return null;
        }

        private static string? ValidateTradingPeriodType(TradingPeriodTypeCreateRequest request)
        {
            if (!MoexDomainRules.IsMarket(request.Market))
                return "market должен быть stock или futures";
            if (string.IsNullOrWhiteSpace(request.TypeCode))
                return "typeCode обязателен";
            if (string.IsNullOrWhiteSpace(request.Title))
                return "title обязателен";
            return null;
        }
    }
}
