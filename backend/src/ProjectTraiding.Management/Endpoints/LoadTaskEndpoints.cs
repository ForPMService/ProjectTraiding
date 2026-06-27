using Npgsql;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Management.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class LoadTaskEndpointsLog;
    public static class LoadTaskEndpoints
    {
        public static IEndpointRouteBuilder MapLoadTaskEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/load-tasks", async (
                LoadTaskCreateRequest request,
                LoadTaskWriter writer,
                ILogger<LoadTaskEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                ValidationResult validation = LoadTaskValidator.Validate(request);
                if (!validation.IsValid)
                {
                    string errors = string.Join("; ", validation.Errors);
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
                    return Results.BadRequest(errors);
                }

                try
                {
                    Guid taskId = await writer.CreateAsync(request, ct);
                    // Идентификатор задачи — uuid; общий ManagementResultDto.Id рассчитан на long,
                    // поэтому отдаём taskId отдельно текстом, а не втискиваем в общий DTO.
                    return Results.Text($"load task created: taskId={taskId}", "text/plain");
                }
                catch (PostgresException ex)
                {
                    ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
                    string? message = ex.SqlState switch
                    {
                        "23503" => "secid не найден среди инструментов (FK)",
                        "23514" => "недопустимое значение market или storage_target (страховка)",
                        _ => null
                    };

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            return routes;
        }
    }
}
