using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Infrastructure.Observability;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Configuration;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddProjectTraidingObservability(this IServiceCollection services)
    {
        // Backward-compatible: no JSONL sink configured.
        services.AddSingleton<ISecretRedactor, DefaultSecretRedactor>();
        services.AddSingleton<JsonConsoleOperationLogger>();
        services.AddSingleton<IOperationLogger>(sp => sp.GetRequiredService<JsonConsoleOperationLogger>());

        return services;
    }

    public static IServiceCollection AddProjectTraidingObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecretRedactor, DefaultSecretRedactor>();

        // Register the console logger as concrete implementation so it can be composed.
        services.AddSingleton<JsonConsoleOperationLogger>();

        // Bind JSONL sink options from configuration (optional)
        services.Configure<JsonlFileSinkOptions>(configuration.GetSection("Observability:Jsonl"));
        services.AddSingleton<JsonlFileOperationSink>();

        // Composite logger: keeps console behavior and adds optional JSONL sink.
        services.AddSingleton<IOperationLogger>(sp =>
        {
            var console = sp.GetRequiredService<JsonConsoleOperationLogger>();
            var sink = sp.GetRequiredService<JsonlFileOperationSink>();
            var logger = sp.GetRequiredService<ILogger<CombinedOperationLogger>>();
            return new CombinedOperationLogger(console, sink, logger);
        });

        return services;
    }
}
