using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ProjectTraiding.Shared.Configuration;

namespace ProjectTraiding.Api.Health;

public sealed class RedisHealthConnectionProvider
{
    private readonly RedisOptions _redisOptions;
    private readonly InfrastructureHealthOptions _infraOptions;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private volatile IConnectionMultiplexer? _multiplexer;

    public RedisHealthConnectionProvider(IOptions<RedisOptions> redisOptions, IOptions<InfrastructureHealthOptions> infraOptions)
    {
        _redisOptions = redisOptions?.Value ?? new RedisOptions();
        _infraOptions = infraOptions?.Value ?? new InfrastructureHealthOptions();
    }

    public async Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var existing = _multiplexer;
        if (existing is not null && existing.IsConnected) return existing;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = _multiplexer;
            if (existing is not null && existing.IsConnected) return existing;

            var config = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectTimeout = _infraOptions.TimeoutMs,
                SyncTimeout = _infraOptions.TimeoutMs
            };
            config.EndPoints.Add(_redisOptions.Host, _redisOptions.Port);

            IConnectionMultiplexer mux = await ConnectionMultiplexer.ConnectAsync(config).ConfigureAwait(false);

            if (mux.IsConnected)
            {
                _multiplexer = mux;
            }

            return _multiplexer ?? mux;
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
