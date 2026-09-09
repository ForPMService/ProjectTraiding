using ProjectTraiding.CustomFeatures.Contracts;
using ProjectTraiding.CustomFeatures.StorageBase.Postgres;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class CbRateEndpointsLog;

    public static class CbRateEndpoints
    {
        private const string FactTypeMeetingScheduled = "meeting_scheduled";
        private const string FactTypeDecisionPublished = "decision_published";
        private const string FactTypeCommunicationPublished = "policy_communication_published";

        public static IEndpointRouteBuilder MapCbRateEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/cbrate/meetings", async (
                CbRateMeetingFactRequest request,
                CbRateMeetingFactWriter writer,
                ILogger<CbRateEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/cbrate/meetings";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                string? error = ValidateFact(request);
                if (error is not null)
                {
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                (Guid id, Guid cycleId) = await writer.CreateAsync(CreateFactCommand(request), ct);
                return Results.Json(
                    new CbRateMeetingFactCreateResponse(id, cycleId),
                    ManagementJsonContext.Default.CbRateMeetingFactCreateResponse);
            });

            routes.MapPost("/management/cbrate/calendar", async (
                CbRateCalendarBatchRequest request,
                CbRateCalendarWriter writer,
                ILogger<CbRateEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/cbrate/calendar";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                string? error = ValidateCalendarBatch(request);
                if (error is not null)
                {
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                List<CbRateCalendarDayUpsertCommand> commands = new(request.Days!.Count);
                for (int index = 0; index < request.Days.Count; index++)
                    commands.Add(CreateCalendarDayCommand(request.Days[index]));

                int rowsWritten = await writer.UpsertDaysAsync(commands, ct);
                return CalendarResponse(rowsWritten);
            });

            routes.MapDelete("/management/cbrate/calendar", async (
                DateOnly? dateFrom,
                DateOnly? dateTill,
                CbRateCalendarWriter writer,
                ILogger<CbRateEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "DELETE /management/cbrate/calendar";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                if (dateFrom is null || dateTill is null)
                {
                    ManagementEndpointLogMessages.ValidationRejected(
                        logger, route, "dateFrom и dateTill обязательны");
                    return Results.BadRequest("dateFrom и dateTill обязательны");
                }

                int rowsWritten = await writer.DeleteRangeAsync(dateFrom.Value, dateTill.Value, ct);
                return CalendarResponse(rowsWritten);
            });

            return routes;
        }

        private static IResult CalendarResponse(int rowsWritten) => Results.Json(
            new CalendarOperationResponse(rowsWritten),
            ManagementJsonContext.Default.CalendarOperationResponse);

        private static string? ValidateFact(CbRateMeetingFactRequest request)
        {
            if (request.FactType != FactTypeMeetingScheduled
                && request.FactType != FactTypeDecisionPublished
                && request.FactType != FactTypeCommunicationPublished)
                return "factType должен быть одним из: meeting_scheduled, decision_published, policy_communication_published";
            if (request.MeetingDate is null)
                return "meetingDate обязателен";
            if (request.KnownAt is null)
                return "knownAt обязателен";
            return null;
        }

        private static string? ValidateCalendarBatch(CbRateCalendarBatchRequest request)
        {
            if (request.Days is null || request.Days.Count == 0)
                return "days обязателен и не может быть пустым";

            for (int index = 0; index < request.Days.Count; index++)
            {
                CbRateCalendarDayRequest? day = request.Days[index];
                if (day is null)
                    return $"days[{index}] обязателен";
                if (day.D is null)
                    return $"days[{index}]: d обязателен";
            }

            return null;
        }

        private static CbRateMeetingFactCreateCommand CreateFactCommand(CbRateMeetingFactRequest request)
        {
            return new CbRateMeetingFactCreateCommand(
                request.CycleId, request.FactType!, request.MeetingDate!.Value, request.KnownAt!.Value,
                request.ScheduledPublicationAt, request.PublishedAt,
                request.RateBefore, request.RateAfter, request.EffectiveFrom,
                request.IsCancelled ?? false);
        }

        private static CbRateCalendarDayUpsertCommand CreateCalendarDayCommand(CbRateCalendarDayRequest request)
        {
            CbRateRateBlock? rate = request.Rate;
            CbRateRoisfixBlock? roisfix = request.Roisfix;
            CbRateRuoniaBlock? ruonia = request.Ruonia;
            CbRateExpertBlock? expert = request.Expert;

            return new CbRateCalendarDayUpsertCommand(
                request.D!.Value,
                rate is not null, rate?.RateAnnounced, rate?.RateEffective,
                roisfix is not null,
                roisfix?.Roisfix1w, roisfix?.Roisfix2w, roisfix?.Roisfix1m, roisfix?.Roisfix2m,
                roisfix?.Roisfix3m, roisfix?.Roisfix6m, roisfix?.Roisfix1y, roisfix?.Roisfix2y,
                roisfix?.KnownAt,
                ruonia is not null, ruonia?.Ruonia, ruonia?.Status, ruonia?.KnownAt,
                expert is not null, expert?.ExpertRate, expert?.Source, expert?.KnownAt);
        }
    }
}
