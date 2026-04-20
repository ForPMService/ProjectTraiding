using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectTraiding.Shared.Configuration;

public static class ProjectTraidingOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddProjectTraidingOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostgresOptions>(configuration.GetSection("Postgres"));
        services.Configure<ClickHouseOptions>(configuration.GetSection("ClickHouse"));
        services.Configure<RedisOptions>(configuration.GetSection("Redis"));
        services.Configure<ObjectStorageOptions>(configuration.GetSection("ObjectStorage"));
        services.Configure<InfrastructureHealthOptions>(configuration.GetSection("InfrastructureHealth"));

        services.PostConfigure<PostgresOptions>(options =>
        {
            var v = Environment.GetEnvironmentVariable("POSTGRES_HOST");
            if (!string.IsNullOrWhiteSpace(v)) options.Host = v;
            v = Environment.GetEnvironmentVariable("POSTGRES_PORT");
            if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var p)) options.Port = p;
            v = Environment.GetEnvironmentVariable("POSTGRES_DB");
            if (!string.IsNullOrWhiteSpace(v)) options.Database = v;
            v = Environment.GetEnvironmentVariable("POSTGRES_USER");
            if (!string.IsNullOrWhiteSpace(v)) options.User = v;
            v = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
            if (!string.IsNullOrWhiteSpace(v)) options.Password = v;
        });

        services.PostConfigure<RedisOptions>(options =>
        {
            var v = Environment.GetEnvironmentVariable("REDIS_HOST");
            if (!string.IsNullOrWhiteSpace(v)) options.Host = v;
            v = Environment.GetEnvironmentVariable("REDIS_PORT");
            if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var p)) options.Port = p;
        });

        services.PostConfigure<ClickHouseOptions>(options =>
        {
            var v = Environment.GetEnvironmentVariable("CLICKHOUSE_HOST");
            if (!string.IsNullOrWhiteSpace(v)) options.Host = v;
            v = Environment.GetEnvironmentVariable("CLICKHOUSE_HTTP_PORT");
            if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var hp)) options.HttpPort = hp;
            v = Environment.GetEnvironmentVariable("CLICKHOUSE_NATIVE_PORT");
            if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var np)) options.NativePort = np;
            v = Environment.GetEnvironmentVariable("CLICKHOUSE_DB");
            if (!string.IsNullOrWhiteSpace(v)) options.Database = v;
            v = Environment.GetEnvironmentVariable("CLICKHOUSE_USER");
            if (!string.IsNullOrWhiteSpace(v)) options.User = v;
            v = Environment.GetEnvironmentVariable("CLICKHOUSE_PASSWORD");
            if (!string.IsNullOrWhiteSpace(v)) options.Password = v;
        });

        services.PostConfigure<ObjectStorageOptions>(options =>
        {
            var v = Environment.GetEnvironmentVariable("OBJECT_STORAGE_PROVIDER");
            if (!string.IsNullOrWhiteSpace(v)) options.Provider = v;
            v = Environment.GetEnvironmentVariable("MINIO_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(v)) options.Endpoint = v;
            v = Environment.GetEnvironmentVariable("MINIO_ACCESS_KEY");
            if (!string.IsNullOrWhiteSpace(v)) options.AccessKey = v;
            v = Environment.GetEnvironmentVariable("MINIO_SECRET_KEY");
            if (!string.IsNullOrWhiteSpace(v)) options.SecretKey = v;
            v = Environment.GetEnvironmentVariable("MINIO_BUCKET_RAW");
            if (!string.IsNullOrWhiteSpace(v)) options.BucketRaw = v;
            v = Environment.GetEnvironmentVariable("MINIO_BUCKET_EXPORTS");
            if (!string.IsNullOrWhiteSpace(v)) options.BucketExports = v;
        });

        services.PostConfigure<InfrastructureHealthOptions>(options =>
        {
            var v = Environment.GetEnvironmentVariable("INFRASTRUCTURE_HEALTH_TIMEOUT_MS");
            if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var t) && t > 0) options.TimeoutMs = t;
        });

        return services;
    }
}
