using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectTraiding.Infrastructure.Health;
using ProjectTraiding.Shared.Configuration;

namespace ProjectTraiding.Infrastructure.Configuration;

public static class InfrastructureHealthServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureHealth(this IServiceCollection services)
    {
        services.AddHttpClient("clickhouse-health").ConfigureHttpClient((sp, client) =>
        {
            var infra = sp.GetRequiredService<IOptions<InfrastructureHealthOptions>>().Value;
            var timeoutMs = infra is not null && infra.TimeoutMs > 0 ? infra.TimeoutMs : 2000;
            client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
        });

        services.AddHttpClient("minio-health").ConfigureHttpClient((sp, client) =>
        {
            var infra = sp.GetRequiredService<IOptions<InfrastructureHealthOptions>>().Value;
            var timeoutMs = infra is not null && infra.TimeoutMs > 0 ? infra.TimeoutMs : 2000;
            client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
        });

        services.AddSingleton(sp =>
        {
            var pg = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = pg.Host,
                Port = pg.Port,
                Database = pg.Database,
                Username = pg.User,
                Password = pg.Password,
                Pooling = true
            };

            return NpgsqlDataSource.Create(csb.ConnectionString);
        });

        services.AddSingleton<RedisHealthConnectionProvider>();
        services.AddSingleton<InfrastructureHealthChecker>();

        return services;
    }
}
