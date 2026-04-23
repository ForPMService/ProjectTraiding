using Microsoft.Extensions.DependencyInjection;
using ProjectTraiding.Infrastructure.Observability;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Configuration;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddProjectTraidingObservability(this IServiceCollection services)
    {
        services.AddSingleton<ISecretRedactor, DefaultSecretRedactor>();
        services.AddSingleton<IOperationLogger, JsonConsoleOperationLogger>();

        return services;
    }
}
