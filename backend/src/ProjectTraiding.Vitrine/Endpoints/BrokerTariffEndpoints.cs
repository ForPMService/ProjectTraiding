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
    internal sealed class BrokerTariffEndpointsLog;
    public static class BrokerTariffEndpoints
    {
        public static IEndpointRouteBuilder MapBrokerTariffEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/tariffs", async (
                BrokerTariffReadQuery query,
                ILogger<BrokerTariffEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /vitrine/tariffs";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                List<VitrineBrokerTariffDto> items = await query.GetAllAsync(ct);
                return Results.Json(items, VitrineJsonContext.Default.ListVitrineBrokerTariffDto);
            });

            return routes;
        }
    }
}
