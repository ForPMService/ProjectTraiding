using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectTraiding.Shared.Configuration;

namespace ProjectTraiding.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProjectTraidingOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostgresOptions>(configuration.GetSection("Postgres"));
        services.Configure<ClickHouseOptions>(configuration.GetSection("ClickHouse"));
        services.Configure<RedisOptions>(configuration.GetSection("Redis"));
        services.Configure<ObjectStorageOptions>(configuration.GetSection("ObjectStorage"));
        services.Configure<InfrastructureHealthOptions>(configuration.GetSection("InfrastructureHealth"));

        return services;
    }
}
