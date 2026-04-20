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

        // Postgres
        (string? message, string? code)? pgIssue = null;
        if (string.IsNullOrWhiteSpace(_pg.Host)) pgIssue = ("host is missing", "missing_configuration");
        else if (_pg.Port <= 0) pgIssue = ("port is invalid", "invalid_configuration");
        else if (string.IsNullOrWhiteSpace(_pg.Database)) pgIssue = ("required value is missing", "missing_configuration");
        else if (string.IsNullOrWhiteSpace(_pg.User)) pgIssue = ("required value is missing", "missing_configuration");
        else if (string.IsNullOrWhiteSpace(_pg.Password)) pgIssue = ("required value is missing", "missing_configuration");

        if (pgIssue != null)
        {
            services.Add(new ServiceHealthItem("postgres-config", "degraded", pgIssue.Value.message, ErrorCode: pgIssue.Value.code));
            overallOk = false;
        }
        else
        {
            services.Add(new ServiceHealthItem("postgres-config", "ok"));
        }

        // ClickHouse
        (string? message, string? code)? chIssue = null;
        if (string.IsNullOrWhiteSpace(_ch.Host)) chIssue = ("host is missing", "missing_configuration");
        else if (_ch.HttpPort <= 0) chIssue = ("port is invalid", "invalid_configuration");
        else if (_ch.NativePort <= 0) chIssue = ("port is invalid", "invalid_configuration");
        else if (string.IsNullOrWhiteSpace(_ch.Database)) chIssue = ("required value is missing", "missing_configuration");
        else if (string.IsNullOrWhiteSpace(_ch.User)) chIssue = ("required value is missing", "missing_configuration");
        else if (string.IsNullOrWhiteSpace(_ch.Password)) chIssue = ("required value is missing", "missing_configuration");

        if (chIssue != null)
        {
            services.Add(new ServiceHealthItem("clickhouse-config", "degraded", chIssue.Value.message, ErrorCode: chIssue.Value.code));
            overallOk = false;
        }
        else
        {
            services.Add(new ServiceHealthItem("clickhouse-config", "ok"));
        }

        // Redis
        (string? message, string? code)? redisIssue = null;
        if (string.IsNullOrWhiteSpace(_redis.Host)) redisIssue = ("host is missing", "missing_configuration");
        else if (_redis.Port <= 0) redisIssue = ("port is invalid", "invalid_configuration");

        if (redisIssue != null)
        {
            services.Add(new ServiceHealthItem("redis-config", "degraded", redisIssue.Value.message, ErrorCode: redisIssue.Value.code));
            overallOk = false;
        }
        else
        {
            services.Add(new ServiceHealthItem("redis-config", "ok"));
        }

        // Object storage
        (string? message, string? code)? objIssue = null;
        if (string.IsNullOrWhiteSpace(_obj.Provider))
        {
            objIssue = ("required value is missing", "missing_configuration");
        }
        else
        {
            var provider = _obj.Provider.Trim().ToLowerInvariant();
            if (provider != "local" && provider != "minio")
            {
                objIssue = ("provider is invalid", "invalid_configuration");
            }
            else if (provider == "local")
            {
                if (string.IsNullOrWhiteSpace(_obj.BucketRaw) || string.IsNullOrWhiteSpace(_obj.BucketExports))
                {
                    objIssue = ("required value is missing", "missing_configuration");
                }
            }
            else if (provider == "minio")
            {
                if (string.IsNullOrWhiteSpace(_obj.Endpoint)) objIssue = ("required value is missing", "missing_configuration");
                else if (string.IsNullOrWhiteSpace(_obj.AccessKey)) objIssue = ("required value is missing", "missing_configuration");
                else if (string.IsNullOrWhiteSpace(_obj.SecretKey)) objIssue = ("required value is missing", "missing_configuration");
                else if (string.IsNullOrWhiteSpace(_obj.BucketRaw) || string.IsNullOrWhiteSpace(_obj.BucketExports)) objIssue = ("required value is missing", "missing_configuration");
            }
        }

        if (objIssue != null)
        {
            services.Add(new ServiceHealthItem("object-storage-config", "degraded", objIssue.Value.message, ErrorCode: objIssue.Value.code));
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
