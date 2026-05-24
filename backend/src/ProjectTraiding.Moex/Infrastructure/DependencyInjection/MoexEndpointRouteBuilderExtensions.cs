using ProjectTraiding.Moex.Endpoints;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection;

public static class MoexEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMoexEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapReferenceEndpoints();
        routes.MapAlgopackEndpoints();
        routes.MapCalendarEndpoints();
        routes.MapRealtimeDebugEndpoints();
        routes.MapRealtimeDiagnosticEndpoints();
        return routes;
    }

    public static IEndpointRouteBuilder MapMoexDebugEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapDebugEndpoints();
        return routes;
    }
}
