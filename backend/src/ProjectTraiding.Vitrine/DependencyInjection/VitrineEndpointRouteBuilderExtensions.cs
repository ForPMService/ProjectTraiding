using Microsoft.AspNetCore.Routing;
using ProjectTraiding.Vitrine.Endpoints;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.DependencyInjection
{
    public static class VitrineEndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapVitrineEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapInstrumentEndpoints();
            routes.MapCalendarEndpoints();
            routes.MapBrokerTariffEndpoints();
            routes.MapInstrumentRelationEndpoints();
            routes.MapStockCardEndpoints();
            routes.MapFuturesCardEndpoints();
            routes.MapInstrumentRelationBySecidEndpoints();
            return routes;
        }
    }
}
