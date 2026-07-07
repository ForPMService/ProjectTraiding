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
    internal sealed class FuturesCardEndpointsLog;
    public static class FuturesCardEndpoints
    {
        public static IEndpointRouteBuilder MapFuturesCardEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/cards/futures/{secid}", async (
                string secid,
                FuturesCardCache cache,
                ILogger<FuturesCardEndpointsLog> logger,
                CancellationToken ct) =>
            {
                string route = $"GET /vitrine/cards/futures/{secid}";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                VitrineFuturesCardDto? card = await cache.GetBySecidAsync(secid, ct);
                if (card is null)
                    return Results.NotFound();
                return Results.Json(card, VitrineJsonContext.Default.VitrineFuturesCardDto);
            });

            return routes;
        }
    }
}
