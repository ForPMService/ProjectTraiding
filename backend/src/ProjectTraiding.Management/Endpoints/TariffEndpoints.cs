using Npgsql;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Management.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class TariffEndpointsLog;
    public static class TariffEndpoints
    {
        public static IEndpointRouteBuilder MapTariffEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/tariffs", async (
                BrokerTariffCreateRequest request,
                BrokerTariffWriter writer,
                ILogger<TariffEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/tariffs";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                ValidationResult validation = BrokerTariffValidator.Validate(request);
                if (!validation.IsValid)
                {
                    string errors = string.Join("; ", validation.Errors);
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
                    return Results.BadRequest(errors);
                }

                try
                {
                    DbWriteResult w = await writer.CreateAsync(request, ct);
                    ManagementResultDto dto = new(
                        Operation: "create_broker_tariff",
                        Target: "moex_broker_tariffs",
                        Status: "ok",
                        Id: w.Id,
                        RowsWritten: w.RowsWritten,
                        ElapsedMs: w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.ManagementResultDto);
                }
                catch (PostgresException ex)
                {
                    ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
                    string? message = ex.SqlState switch
                    {
                        "23514" => "market должен быть одним из: stock, futures (страховка)",
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
