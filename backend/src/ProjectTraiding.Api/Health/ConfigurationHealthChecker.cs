using System.Collections.Generic;
using Microsoft.Extensions.Options;
using ProjectTraiding.Contracts.Health;
using ProjectTraiding.Shared.Configuration;

namespace ProjectTraiding.Api.Health;

public sealed class ConfigurationHealthChecker
{
    private readonly PostgresOptions _pg;
    private readonly ClickHouseOptions _ch;
    private readonly RedisOptions _redis;
    private readonly ObjectStorageOptions _obj;

    public ConfigurationHealthChecker(
        IOptions<PostgresOptions> pgOptions,
        IOptions<ClickHouseOptions> chOptions,
        IOptions<RedisOptions> redisOptions,
        IOptions<ObjectStorageOptions> objOptions)
    {
        _pg = pgOptions.Value;
        _ch = chOptions.Value;
        _redis = redisOptions.Value;
        _obj = objOptions.Value;
    }

    public HealthStatusResponse Check()
    {
        var services = new List<ServiceHealthItem>
        {
            new ServiceHealthItem("api", "ok")
        };

        var overallOk = true;

        if (string.IsNullOrWhiteSpace(_pg.Host) || _pg.Port <= 0 || string.IsNullOrWhiteSpace(_pg.Database) || string.IsNullOrWhiteSpace(_pg.User))
        {
            services.Add(new ServiceHealthItem("postgres-config", "degraded", "missing required Postgres configuration"));
            overallOk = false;
        }
        else
        {
            services.Add(new ServiceHealthItem("postgres-config", "ok"));
        }

        if (string.IsNullOrWhiteSpace(_ch.Host) || _ch.HttpPort <= 0 || _ch.NativePort <= 0 || string.IsNullOrWhiteSpace(_ch.Database) || string.IsNullOrWhiteSpace(_ch.User))
        {
            services.Add(new ServiceHealthItem("clickhouse-config", "degraded", "missing required ClickHouse configuration"));
            overallOk = false;
        }
        else
        {
            services.Add(new ServiceHealthItem("clickhouse-config", "ok"));
        }

        if (string.IsNullOrWhiteSpace(_redis.Host) || _redis.Port <= 0)
        {
            services.Add(new ServiceHealthItem("redis-config", "degraded", "missing required Redis configuration"));
            overallOk = false;
        }
        else
        {
            services.Add(new ServiceHealthItem("redis-config", "ok"));
        }

        if (string.IsNullOrWhiteSpace(_obj.Provider) || string.IsNullOrWhiteSpace(_obj.Endpoint) || string.IsNullOrWhiteSpace(_obj.BucketRaw) || string.IsNullOrWhiteSpace(_obj.BucketExports))
        {
            services.Add(new ServiceHealthItem("object-storage-config", "degraded", "missing required object storage configuration"));
            overallOk = false;
        }
        else
        {
            services.Add(new ServiceHealthItem("object-storage-config", "ok"));
        }

        var status = overallOk ? "ready" : "degraded";
        return new HealthStatusResponse(status, services);
    }
}
