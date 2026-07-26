using ProjectTraiding.Diagnostics.Endpoints;

namespace ProjectTraiding.Diagnostics.DependencyInjection;

public static class DiagnosticsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapAlgopackEndpoints();
        routes.MapRealtimeDebugEndpoints();
        routes.MapRealtimeDiagnosticEndpoints();
        routes.MapDiagnosticDebugEndpoints();
        routes.MapWebSocketProbeEndpoints();

        return routes;
    }
}
