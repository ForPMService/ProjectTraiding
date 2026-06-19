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
    internal sealed class InstrumentRelationEndpointsLog;
    public static class InstrumentRelationEndpoints
    {
        public static IEndpointRouteBuilder MapInstrumentRelationEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/relations", async (
                InstrumentRelationReadQuery query,
                ILogger<InstrumentRelationEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /vitrine/relations";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                List<VitrineInstrumentRelationDto> items = await query.GetAllAsync(ct);
                return Results.Json(items, VitrineJsonContext.Default.ListVitrineInstrumentRelationDto);
            });

            return routes;
        }
    }
}
