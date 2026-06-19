using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Endpoints
{
    internal sealed class StatusEndpointsLog;
    public static class StatusEndpoints
    {
        public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/status", async (
                StatusReadQuery query,
                ILogger<StatusEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /vitrine/status";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                VitrineStatusDto status = await query.GetAsync(ct);
                return Results.Json(status, VitrineJsonContext.Default.VitrineStatusDto);
            });

            return routes;
        }
    }
}
