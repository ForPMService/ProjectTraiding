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
    internal sealed class InstrumentRelationBySecidEndpointsLog;
    public static class InstrumentRelationBySecidEndpoints
    {
        public static IEndpointRouteBuilder MapInstrumentRelationBySecidEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/relations/{secid}", async (
                string secid,
                InstrumentRelationBySecidReadQuery query,
                ILogger<InstrumentRelationBySecidEndpointsLog> logger,
                CancellationToken ct) =>
            {
                string route = $"GET /vitrine/relations/{secid}";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                List<VitrineInstrumentRelationDto> items = await query.GetBySecidAsync(secid, ct);
                return Results.Json(items, VitrineJsonContext.Default.ListVitrineInstrumentRelationDto);
            });

            return routes;
        }
    }
}
