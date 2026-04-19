using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectTraiding.Contracts.Health;
using ProjectTraiding.Shared.Configuration;

namespace ProjectTraiding.Api.Health;

public sealed class InfrastructureHealthChecker
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RedisHealthConnectionProvider _redisProvider;
    private readonly InfrastructureHealthOptions _infraOptions;

    public InfrastructureHealthChecker(
        NpgsqlDataSource dataSource,
        RedisHealthConnectionProvider redisProvider,
        IOptions<InfrastructureHealthOptions> infraOptions)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _infraOptions = infraOptions?.Value ?? new InfrastructureHealthOptions();
    }

    public async Task<IReadOnlyList<ServiceHealthItem>> CheckAsync(CancellationToken cancellationToken)
    {
        var pgTask = CheckPostgresAsync(cancellationToken);
        var redisTask = CheckRedisAsync(cancellationToken);

        await Task.WhenAll(pgTask, redisTask).ConfigureAwait(false);

        var pgResult = await pgTask.ConfigureAwait(false);
        var redisResult = await redisTask.ConfigureAwait(false);

        return new List<ServiceHealthItem> { pgResult, redisResult };
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
        catch
        {
            sw.Stop();
            return new ServiceHealthItem("postgres", "unhealthy", "connection failed", sw.ElapsedMilliseconds, "connection_failed");
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
}
