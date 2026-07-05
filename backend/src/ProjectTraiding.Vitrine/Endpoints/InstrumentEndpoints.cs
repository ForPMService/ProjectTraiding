using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using ProjectTraiding.Vitrine.StorageBase.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Endpoints
{
    internal sealed class InstrumentEndpointsLog;
    public static class InstrumentEndpoints
    {
        public static IEndpointRouteBuilder MapInstrumentEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/instruments", async (
                InstrumentCatalogCache cache,               // ← было InstrumentReadQuery query
                ILogger<InstrumentEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /vitrine/instruments";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                List<VitrineInstrumentDto> items = await cache.GetAllAsync(ct);
                return Results.Json(items, VitrineJsonContext.Default.ListVitrineInstrumentDto);
            });
            return routes;
        }
    }
}
