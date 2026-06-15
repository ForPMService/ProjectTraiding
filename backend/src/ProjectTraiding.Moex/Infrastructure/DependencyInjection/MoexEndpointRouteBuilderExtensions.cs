using ProjectTraiding.Moex.Endpoints;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection;

public static class MoexEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMoexSyncEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapInstrumentCardLoadEndpoints();
        routes.MapCalendarLoadEndpoints();
        return routes;
    }

    public static IEndpointRouteBuilder MapMoexDiagnosticEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapAlgopackEndpoints();
        routes.MapCalendarEndpoints();
        routes.MapRealtimeDebugEndpoints();
        routes.MapRealtimeDiagnosticEndpoints();
        routes.MapDiagnosticDebugEndpoints();
        return routes;
    }

    public static IEndpointRouteBuilder MapMoexTemporaryDebugEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapTemporaryDebugEndpoints();
        routes.MapMoexProbeEndpoints();
        return routes;
    }
}
