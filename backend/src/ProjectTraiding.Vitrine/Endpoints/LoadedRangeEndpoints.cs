using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Endpoints
{
    internal sealed class LoadedRangeEndpointsLog;
    public static class LoadedRangeEndpoints
    {
        public static IEndpointRouteBuilder MapLoadedRangeEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/loaded-ranges/{secid}", async (
                string secid,
                LoadedRangeCache cache,
                ILogger<LoadedRangeEndpointsLog> logger,
                CancellationToken ct) =>
            {
                string route = $"GET /vitrine/loaded-ranges/{secid}";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                List<VitrineLoadedRangeDto> items = await cache.GetBySecidAsync(secid, ct);
                return Results.Json(items, VitrineJsonContext.Default.ListVitrineLoadedRangeDto);
            });

            return routes;
        }
    }
}
