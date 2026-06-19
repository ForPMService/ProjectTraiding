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
    internal sealed class StockCardEndpointsLog;
    public static class StockCardEndpoints
    {
        public static IEndpointRouteBuilder MapStockCardEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/cards/stock/{secid}", async (
                string secid,
                StockCardReadQuery query,
                ILogger<StockCardEndpointsLog> logger,
                CancellationToken ct) =>
            {
                string route = $"GET /vitrine/cards/stock/{secid}";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                VitrineStockCardDto? card = await query.GetBySecidAsync(secid, ct);
                if (card is null)
                    return Results.NotFound();
                return Results.Json(card, VitrineJsonContext.Default.VitrineStockCardDto);
            });

            return routes;
        }
    }
}
