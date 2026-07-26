using ProjectTraiding.Diagnostics.Options;
using ProjectTraiding.Diagnostics.Probe;

namespace ProjectTraiding.Diagnostics.DependencyInjection;

public static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddProjectTraidingDiagnostics(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WebSocketProbeOptions>()
            .Bind(configuration.GetSection("Diagnostics:WebSocketProbe"));

        services.AddTransient<MoexWebSocketProbeClient>();

        return services;
    }
}
