using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectTraiding.Contracts.Health;
using ProjectTraiding.Shared.Configuration;

namespace ProjectTraiding.Api.Health;

public sealed class InfrastructureHealthChecker
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RedisHealthConnectionProvider _redisProvider;
    private readonly InfrastructureHealthOptions _infraOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClickHouseOptions _clickHouseOptions;
    private readonly ObjectStorageOptions _objectStorageOptions;
    private readonly ILogger<InfrastructureHealthChecker> _logger;
    private readonly PostgresOptions _postgresOptions;

    public InfrastructureHealthChecker(
        NpgsqlDataSource dataSource,
        RedisHealthConnectionProvider redisProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<InfrastructureHealthOptions> infraOptions,
        IOptions<ClickHouseOptions> clickHouseOptions,
        IOptions<ObjectStorageOptions> objectStorageOptions,
        IOptions<PostgresOptions> postgresOptions,
        ILogger<InfrastructureHealthChecker> logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _infraOptions = infraOptions?.Value ?? new InfrastructureHealthOptions();
        _clickHouseOptions = clickHouseOptions?.Value ?? new ClickHouseOptions();
        _objectStorageOptions = objectStorageOptions?.Value ?? new ObjectStorageOptions();
        _postgresOptions = postgresOptions?.Value ?? new PostgresOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ServiceHealthItem>> CheckAsync(CancellationToken cancellationToken)
    {
        var pgTask = CheckPostgresAsync(cancellationToken);
        var redisTask = CheckRedisAsync(cancellationToken);
        var clickhouseTask = CheckClickHouseAsync(cancellationToken);
        var objectStorageTask = CheckObjectStorageAsync(cancellationToken);

        var results = await Task.WhenAll(pgTask, redisTask, clickhouseTask, objectStorageTask).ConfigureAwait(false);

        return results;
    }

    private async Task<ServiceHealthItem> CheckPostgresAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_infraOptions.TimeoutMs);

            await using var conn = await _dataSource.OpenConnectionAsync(cts.Token).ConfigureAwait(false);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = Math.Max(1, _infraOptions.TimeoutMs / 1000);
            var _ = await cmd.ExecuteScalarAsync(cts.Token).ConfigureAwait(false);

            sw.Stop();
            return new ServiceHealthItem("postgres", "ok", null, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ServiceHealthItem("postgres", "unhealthy", "timeout", sw.ElapsedMilliseconds, "timeout");
        }
        catch (Exception ex)
        {
            sw.Stop();

            var exceptionType = ex.GetType().FullName ?? string.Empty;
            string sqlState = null;
            if (ex is PostgresException pgEx)
            {
                sqlState = pgEx.SqlState;
            }

            _logger.LogWarning(
                "PostgreSQL health check failed. ExceptionType={ExceptionType} SqlState={SqlState} Host={Host} Port={Port} Database={Database} User={User} PasswordSet={PasswordSet}",
                exceptionType,
                sqlState ?? string.Empty,
                _postgresOptions.Host ?? string.Empty,
                _postgresOptions.Port,
                _postgresOptions.Database ?? string.Empty,
                _postgresOptions.User ?? string.Empty,
                !string.IsNullOrWhiteSpace(_postgresOptions.Password));

            return new ServiceHealthItem(
                "postgres",
                "unhealthy",
                "connection failed",
                sw.ElapsedMilliseconds,
                "connection_failed");
        }
    }

    private async Task<ServiceHealthItem> CheckRedisAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_infraOptions.TimeoutMs);

            var mux = await _redisProvider.GetConnectionAsync(cts.Token).ConfigureAwait(false);

            if (mux is null || !mux.IsConnected)
            {
                sw.Stop();
                return new ServiceHealthItem("redis", "unhealthy", "connection failed", sw.ElapsedMilliseconds, "connection_failed");
            }

            var db = mux.GetDatabase();
            var pingTask = db.PingAsync();

            var completed = await Task.WhenAny(pingTask, Task.Delay(_infraOptions.TimeoutMs, cts.Token)).ConfigureAwait(false);
            if (completed != pingTask)
            {
                sw.Stop();
                return new ServiceHealthItem("redis", "unhealthy", "timeout", sw.ElapsedMilliseconds, "timeout");
            }

            await pingTask.ConfigureAwait(false);

            sw.Stop();
            return new ServiceHealthItem("redis", "ok", null, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ServiceHealthItem("redis", "unhealthy", "timeout", sw.ElapsedMilliseconds, "timeout");
        }
        catch
        {
            sw.Stop();
            return new ServiceHealthItem("redis", "unhealthy", "connection failed", sw.ElapsedMilliseconds, "connection_failed");
        }
    }

    private async Task<ServiceHealthItem> CheckClickHouseAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("clickhouse-health");
            var host = _clickHouseOptions.Host ?? string.Empty;
            var port = _clickHouseOptions.HttpPort;
            var url = $"http://{host}:{port}/ping";

            var resp = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (resp.IsSuccessStatusCode)
            {
                return new ServiceHealthItem("clickhouse", "ok", null, sw.ElapsedMilliseconds, null);
            }

            return new ServiceHealthItem("clickhouse", "unhealthy", "http error", sw.ElapsedMilliseconds, "http_error");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ServiceHealthItem("clickhouse", "unhealthy", "timeout", sw.ElapsedMilliseconds, "timeout");
        }
        catch (HttpRequestException)
        {
            sw.Stop();
            return new ServiceHealthItem("clickhouse", "unhealthy", "connection failed", sw.ElapsedMilliseconds, "connection_failed");
        }
        catch
        {
            sw.Stop();
            return new ServiceHealthItem("clickhouse", "unhealthy", "connection failed", sw.ElapsedMilliseconds, "connection_failed");
        }
    }

    private async Task<ServiceHealthItem> CheckObjectStorageAsync(CancellationToken cancellationToken)
    {
        // Local provider: do not contact MinIO
        if (string.Equals(_objectStorageOptions.Provider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceHealthItem("object-storage", "ok", "local provider", null, null);
        }

        if (string.Equals(_objectStorageOptions.Provider, "minio", StringComparison.OrdinalIgnoreCase))
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var client = _httpClientFactory.CreateClient("minio-health");
                var endpoint = (_objectStorageOptions.Endpoint ?? string.Empty).TrimEnd('/');
                var url = $"{endpoint}/minio/health/live";

                var resp = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (resp.IsSuccessStatusCode)
                {
                    return new ServiceHealthItem("minio", "ok", null, sw.ElapsedMilliseconds, null);
                }

                return new ServiceHealthItem("minio", "unhealthy", "http error", sw.ElapsedMilliseconds, "http_error");
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new ServiceHealthItem("minio", "unhealthy", "timeout", sw.ElapsedMilliseconds, "timeout");
            }
            catch (HttpRequestException)
            {
                sw.Stop();
                return new ServiceHealthItem("minio", "unhealthy", "connection failed", sw.ElapsedMilliseconds, "connection_failed");
            }
            catch
            {
                sw.Stop();
                return new ServiceHealthItem("minio", "unhealthy", "connection failed", sw.ElapsedMilliseconds, "connection_failed");
            }
        }

        // Unknown provider
        return new ServiceHealthItem("object-storage", "unhealthy", "unknown provider", null, "unknown_provider");
    }
}
