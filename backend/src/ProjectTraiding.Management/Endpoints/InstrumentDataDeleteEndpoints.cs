using Npgsql;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class InstrumentDataDeleteEndpointsLog;

    public static class InstrumentDataDeleteEndpoints
    {
        public static IEndpointRouteBuilder MapInstrumentDataDeleteEndpoints(
            this IEndpointRouteBuilder routes)
        {
            // Сегмент data обязателен: он говорит, что удаляются данные инструмента,
            // а не сам инструмент. Карточка, справочные детали, связи, тарифы и
            // календарь остаются на месте.
            routes.MapPost("/management/instruments/{secid}/data/delete", async (
                string secid,
                InstrumentDeletionWriter writer,
                ILogger<InstrumentDataDeleteEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/instruments/{secid}/data/delete";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                if (string.IsNullOrWhiteSpace(secid))
                {
                    const string error = "secid обязателен";
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                try
                {
                    Guid? deletionId = await writer.TryStartAsync(secid, ct);
                    if (deletionId is null)
                    {
                        const string error = "удаление данных этого инструмента уже начато и не закрыто.";
                        ManagementEndpointLogMessages.InstrumentDataDeleteRejected(
                            logger, route, secid, "already_deleting");
                        return Results.Text(
                            error,
                            "text/plain",
                            statusCode: StatusCodes.Status409Conflict);
                    }

                    InstrumentDataDeleteAcceptedResponse response = new(
                        Secid: secid,
                        DeletionId: deletionId.Value,
                        Status: "accepted");
                    return Results.Json(
                        response,
                        ManagementJsonContext.Default.InstrumentDataDeleteAcceptedResponse,
                        statusCode: StatusCodes.Status202Accepted);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapDeletion(logger, route, ex);
                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapGet("/management/instruments/{secid}/data/delete/status", async (
                string secid,
                InstrumentDeletionStatusReader reader,
                ILogger<InstrumentDataDeleteEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/instruments/{secid}/data/delete/status";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                if (string.IsNullOrWhiteSpace(secid))
                {
                    const string error = "secid обязателен";
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                InstrumentDeletionStatus? status = await reader.GetLatestAsync(secid, ct);
                if (status is null)
                    return Results.NotFound("заявка на удаление данных инструмента не найдена");

                InstrumentDataDeleteStatusResponse response = new(
                    Secid: status.Secid,
                    Status: status.Status,
                    CreatedAt: status.CreatedAt,
                    ClaimedAt: status.ClaimedAt,
                    NextAttemptAt: status.NextAttemptAt,
                    ErrorMessage: status.ErrorMessage);
                return Results.Json(
                    response,
                    ManagementJsonContext.Default.InstrumentDataDeleteStatusResponse);
            });

            return routes;
        }
    }
}
