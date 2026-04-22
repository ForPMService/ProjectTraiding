using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ProjectTraiding.Shared.Configuration;

namespace ProjectTraiding.Infrastructure.Health;

public sealed class RedisHealthConnectionProvider : IDisposable
{
    private readonly RedisOptions _redisOptions;
    private readonly InfrastructureHealthOptions _infraOptions;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IConnectionMultiplexer? _multiplexer;

    public RedisHealthConnectionProvider(IOptions<RedisOptions> redisOptions, IOptions<InfrastructureHealthOptions> infraOptions)
    {
        _redisOptions = redisOptions?.Value ?? new RedisOptions();
        _infraOptions = infraOptions?.Value ?? new InfrastructureHealthOptions();
    }

    public async Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var existing = _multiplexer;
        if (existing is not null) return existing;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = _multiplexer;
            if (existing is not null) return existing;

            var config = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectTimeout = _infraOptions.TimeoutMs,
                SyncTimeout = _infraOptions.TimeoutMs
            };
            config.EndPoints.Add(_redisOptions.Host, _redisOptions.Port);

            IConnectionMultiplexer mux;
            try
            {
                mux = await ConnectionMultiplexer.ConnectAsync(config).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }

            _multiplexer = mux;

            return mux;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        var mux = Interlocked.Exchange(ref _multiplexer, null);
        if (mux is not null)
        {
            try
            {
                mux.Close();
            }
            catch { }

            try
            {
                mux.Dispose();
            }
            catch { }
        }

        try
        {
            _semaphore.Dispose();
        }
        catch { }
    }
}
