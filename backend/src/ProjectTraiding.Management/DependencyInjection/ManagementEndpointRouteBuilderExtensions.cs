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
            // routes.MapTariffEndpoints();   // шаг 5
            return routes;
        }
    }
}
