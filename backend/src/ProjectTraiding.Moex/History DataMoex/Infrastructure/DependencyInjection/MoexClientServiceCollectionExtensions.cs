using History_DataMoex.Clients;
using History_DataMoex.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using System.Net;
using System.Threading.RateLimiting;
namespace History_DataMoex.Infrastructure.DependencyInjection;

public static class MoexClientServiceCollectionExtensions
{
    private const int MaxRetryAttempts = 5;
    public static IServiceCollection AddMoexClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        

        services.AddOptions<MoexOptions>()
            .Bind(configuration.GetSection("Moex"));

        // ══════════════════════════════════════════════
        // Rate Limiter — один на все MOEX-клиенты.
        // Лимит MOEX на IP, не на endpoint, поэтому один limiter на процесс.
        // ══════════════════════════════════════════════
        services.AddSingleton<RateLimiter>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = options.MaxRequestsPerSecond,
                TokensPerPeriod = options.MaxRequestsPerSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = options.RateLimitQueueLimit,
            });
        });

        // ══════════════════════════════════════════════
        // ISS Client
        // ══════════════════════════════════════════════
        services.AddHttpClient<MoexHttpIssClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = options.RequestTimeout;

        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            };
        })
        .AddHttpMessageHandler(sp => new MoexRateLimitHandler(            // ← НОВОЕ
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()))
        .AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.Iss))
        .AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexHttpIssClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.Iss);
        });



        // ══════════════════════════════════════════════
        // Algopack Client
        // ══════════════════════════════════════════════
        services.AddHttpClient<MoexHttpAlgClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = options.RequestTimeout;
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            };
        })
        .AddHttpMessageHandler(sp => new MoexRateLimitHandler(            // ← НОВОЕ
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()))
        .AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.Algopack))
        .AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexHttpAlgClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.Algopack);
        });

        // ══════════════════════════════════════════════
        // Calendar Client
        // ══════════════════════════════════════════════
        services.AddHttpClient<MoexHttpCalendarClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = options.RequestTimeout;
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            };
        })
        .AddHttpMessageHandler(sp => new MoexRateLimitHandler(            // ← НОВОЕ
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()))
        .AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.Calendar))
        .AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexHttpCalendarClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.Calendar);
        });
        // ─────────────────────────────────────────────────────────────────

        // ══════════════════════════════════════════════
        // Realtime REST Client
        // ISS base URL (публичный, без API-ключа).
        // Общий rate limiter, logging handler, Polly resilience.
        // ══════════════════════════════════════════════
        services.AddHttpClient<MoexRealtimeRestClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = options.RequestTimeout;
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            };
        })
        .AddHttpMessageHandler(sp => new MoexRateLimitHandler(
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()))
        .AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.RealtimeRest))
        .AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexRealtimeRestClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.RealtimeRest);
        });
        return services;
    }

    // ══════════════════════════════════════════════
    // Общая настройка resilience (timeout, retry, circuit breaker)
    // ══════════════════════════════════════════════
    private static void ConfigureStandardResilience(HttpStandardResilienceOptions options)
    {
        // ── Timeout budget ──
        // TotalRequestTimeout = 10 мин (весь запрос включая все retry).
        // AttemptTimeout = 2 мин (одна попытка).
        // maxDelay Retry-After = 2 мин (ожидание перед retry при 429).
        // Худший случай одного retry-цикла: 2 мин (wait) + 2 мин (attempt) = 4 мин.
        // При TotalRequestTimeout = 10 мин реально возможны 2–3 retry при длинном Retry-After,
        // а не 5 (дефолт MaxRetryAttempts). Это осознанное поведение:
        // лучше отдать управление вызывающему коду, чем зависнуть на 20 минут.
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(10);
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
        options.Retry.MaxRetryAttempts = MaxRetryAttempts;

        // Учитываем Retry-After из ответа сервера при 429 (rate limit).
        // Если заголовок присутствует — используем его значение вместо
        // дефолтного exponential backoff, чтобы не бомбардировать MOEX.
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result is HttpResponseMessage { StatusCode: (HttpStatusCode)429 } resp)
            {
                var delay = HttpClientHelpers.GetRetryAfterForPolly(resp, TimeSpan.FromMinutes(2));
                if (delay is not null)
                    return ValueTask.FromResult<TimeSpan?>(delay);
            }
            return ValueTask.FromResult<TimeSpan?>(null);
        };
    }

    // ══════════════════════════════════════════════
    // OnRetry handler — логирует каждую retry-попытку
    // ══════════════════════════════════════════════
    private static ValueTask OnRetryHandler(
        Polly.Retry.OnRetryArguments<HttpResponseMessage> args,
        ILogger logger,
        string source)
    {
        HttpStatusCode? statusCode = args.Outcome.Result?.StatusCode;
        string endpoint = args.Outcome.Result?.RequestMessage?.RequestUri?.PathAndQuery ?? "unknown";

        string errorType = statusCode switch
        {
            HttpStatusCode.TooManyRequests => "rate_limit",
            HttpStatusCode.InternalServerError => "server_error",
            HttpStatusCode.BadGateway => "server_error",
            HttpStatusCode.ServiceUnavailable => "server_error",
            HttpStatusCode.GatewayTimeout => "server_error",
            null when args.Outcome.Exception is TimeoutRejectedException => "timeout",
            null when args.Outcome.Exception is TaskCanceledException => "timeout",
            null when args.Outcome.Exception is HttpRequestException => "transport_error",
            _ => "unknown"
        };

        MoexLogMessages.RetryAttempt(
            logger,
            source,
            endpoint,
            args.AttemptNumber+1,
            MaxRetryAttempts,
            errorType,
            args.RetryDelay,
            statusCode);

        return default;
    }

    
}