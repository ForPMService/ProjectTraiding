using Npgsql;
using ProjectTraiding.CustomFeatures.Contracts;
using ProjectTraiding.CustomFeatures.StorageBase.Postgres;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class DividendEventEndpointsLog;

    public static class DividendEventEndpoints
    {
        public static IEndpointRouteBuilder MapDividendEventEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/dividends", async (
                DividendEventBatchCreateRequest request,
                DividendEventWriter writer,
                ILogger<DividendEventEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/dividends";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                string? error = ValidateDividendEventBatch(request);
                if (error is not null)
                {
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                try
                {
                    List<DividendEventCreateCommand> commands = new(request.Events.Count);
                    for (int index = 0; index < request.Events.Count; index++)
                        commands.Add(CreateDividendEventCommand(request.Events[index]));
                    IReadOnlyList<Guid> ids = await writer.CreateManyAsync(commands, ct);
                    return Results.Json(
                        new DividendEventBatchCreateResponse(ids),
                        ManagementJsonContext.Default.DividendEventBatchCreateResponse);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapDividendEvent(logger, route, ex);
                    if (message is null)
                        throw;
                    return Results.BadRequest(message);
                }
            });

            routes.MapPut("/management/dividends/{id}", async (
                Guid id,
                DividendEventUpdateRequest request,
                DividendEventWriter writer,
                ILogger<DividendEventEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "PUT /management/dividends/{id}";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                string? error = ValidateDividendEvent(request);
                if (error is not null)
                {
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                try
                {
                    int rowsWritten = await writer.UpdateAsync(
                        CreateDividendEventUpdateCommand(id, request), ct);
                    if (rowsWritten == 0)
                        return Results.NotFound("Дивидендное событие не найдено.");
                    return CalendarResponse(rowsWritten);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapDividendEvent(logger, route, ex);
                    if (message is null)
                        throw;
                    return Results.BadRequest(message);
                }
            });

            routes.MapDelete("/management/dividends/{id}", async (
                Guid id,
                DividendEventWriter writer,
                ILogger<DividendEventEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "DELETE /management/dividends/{id}";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                int rowsWritten = await writer.DeleteAsync(id, ct);
                if (rowsWritten == 0)
                    return Results.NotFound("Дивидендное событие не найдено.");
                return CalendarResponse(rowsWritten);
            });

            return routes;
        }

        private static IResult CalendarResponse(int rowsWritten) => Results.Json(
            new CalendarOperationResponse(rowsWritten),
            ManagementJsonContext.Default.CalendarOperationResponse);

        private static string? ValidateDividendEventBatch(DividendEventBatchCreateRequest request)
        {
            if (request.Events is null || request.Events.Count == 0)
                return "events обязателен и не может быть пустым";

            for (int index = 0; index < request.Events.Count; index++)
            {
                DividendEventCreateRequest? dividendEvent = request.Events[index];
                if (dividendEvent is null)
                    return $"events[{index}] обязателен";

                string? error = ValidateDividendEvent(dividendEvent);
                if (error is not null)
                    return $"events[{index}]: {error}";
            }

            return null;
        }

        private static string? ValidateDividendEvent(DividendEventCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Secid))
                return "secid обязателен";
            if (string.IsNullOrWhiteSpace(request.EventType))
                return "eventType обязателен";
            if (request.EventDate is null)
                return "eventDate обязателен";
            if (request.KnownAt is null)
                return "knownAt обязателен";
            if (request.DividendAmount is not null && string.IsNullOrWhiteSpace(request.Currency))
                return "currency обязателен при dividendAmount";
            return null;
        }

        private static string? ValidateDividendEvent(DividendEventUpdateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Secid))
                return "secid обязателен";
            if (string.IsNullOrWhiteSpace(request.EventType))
                return "eventType обязателен";
            if (request.EventDate is null)
                return "eventDate обязателен";
            if (request.KnownAt is null)
                return "knownAt обязателен";
            if (request.DividendAmount is not null && string.IsNullOrWhiteSpace(request.Currency))
                return "currency обязателен при dividendAmount";
            return null;
        }

        private static DividendEventCreateCommand CreateDividendEventCommand(
            DividendEventCreateRequest request)
        {
            return new DividendEventCreateCommand(
                request.Secid!, request.EventType!, request.EventDate!.Value, request.KnownAt!.Value,
                request.IsCancelled ?? false, request.DividendAmount, request.Currency,
                request.RecordDate, request.LastEligibleTradeDate, request.PaymentDate, request.SourceNote);
        }

        private static DividendEventUpdateCommand CreateDividendEventUpdateCommand(
            Guid id,
            DividendEventUpdateRequest request)
        {
            return new DividendEventUpdateCommand(
                id, request.Secid!, request.EventType!, request.EventDate!.Value, request.KnownAt!.Value,
                request.DividendAmount, request.Currency, request.RecordDate,
                request.LastEligibleTradeDate, request.PaymentDate, request.SourceNote);
        }
    }
}
