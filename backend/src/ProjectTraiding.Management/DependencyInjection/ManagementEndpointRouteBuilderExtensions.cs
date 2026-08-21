using ProjectTraiding.Management.Endpoints;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.DependencyInjection
{
    public static class ManagementEndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapManagementEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapRelationEndpoints();
            routes.MapTariffEndpoints();
            routes.MapLoadTaskEndpoints();
            routes.MapRealtimeSubscriptionEndpoints();
            routes.MapInstrumentDataDeleteEndpoints();
            routes.MapCalendarEndpoints();
            routes.MapInstrumentCardLoadEndpoints();
            return routes;
        }
    }
}
