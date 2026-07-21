using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using System.Net;
using System.Threading.RateLimiting;
namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection;

public static class MoexClientServiceCollectionExtensions
{
    private const int MaxRetryAttempts = 5;
    public static IServiceCollection AddMoexClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        

        services.AddOptions<MoexOptions>()
            .Bind(configuration.GetSection("Moex"));
        MoexOptions moexOptions = configuration.GetSection("Moex").Get<MoexOptions>() ?? new MoexOptions();
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
        // ══════════════════════════════════════════════
        // ISS Client
        // Порядок обработчиков: журналирование → устойчивость → ограничитель → сокет.
        // Ограничитель ниже слоя устойчивости, поэтому каждая повторная попытка проходит
        // через него и расходует жетон (правка Г3, соответствие контракту 6.2).
        // ══════════════════════════════════════════════
        IHttpClientBuilder issClientBuilder = services.AddHttpClient<MoexHttpIssClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = Timeout.InfiniteTimeSpan;
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
        });

        issClientBuilder.AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.Iss));

        issClientBuilder.AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options, moexOptions);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexHttpIssClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.Iss);
        });

        issClientBuilder.AddHttpMessageHandler(sp => new MoexRateLimitHandler(
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()));



        // ══════════════════════════════════════════════
        // Algopack Client
        // ══════════════════════════════════════════════
        // ══════════════════════════════════════════════
        // Algopack Client
        // Порядок: журналирование → устойчивость → ограничитель → сокет (правка Г3).
        // ══════════════════════════════════════════════
        IHttpClientBuilder algClientBuilder = services.AddHttpClient<MoexHttpAlgClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = Timeout.InfiniteTimeSpan;
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
        });

        algClientBuilder.AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.Algopack));

        algClientBuilder.AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options, moexOptions);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexHttpAlgClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.Algopack);
        });

        algClientBuilder.AddHttpMessageHandler(sp => new MoexRateLimitHandler(
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()));

        // ══════════════════════════════════════════════
        // Calendar Client
        // ══════════════════════════════════════════════
        // ══════════════════════════════════════════════
        // Calendar Client
        // Порядок: журналирование → устойчивость → ограничитель → сокет (правка Г3).
        // ══════════════════════════════════════════════
        IHttpClientBuilder calendarClientBuilder = services.AddHttpClient<MoexHttpCalendarClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = Timeout.InfiniteTimeSpan;
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
        });

        calendarClientBuilder.AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.Calendar));

        calendarClientBuilder.AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options, moexOptions);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexHttpCalendarClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.Calendar);
        });

        calendarClientBuilder.AddHttpMessageHandler(sp => new MoexRateLimitHandler(
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()));
        // ─────────────────────────────────────────────────────────────────

        // ══════════════════════════════════════════════
        // Realtime REST Client
        // APIM base URL (платный, Authorization: Bearer из Moex:AlgKey).
        // Порядок: журналирование → устойчивость → ограничитель → сокет (правка Г3).
        // ══════════════════════════════════════════════
        IHttpClientBuilder realtimeClientBuilder = services.AddHttpClient<MoexRealtimeRestClient>((sp, client) =>
        {
            MoexOptions options = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
            client.Timeout = Timeout.InfiniteTimeSpan;
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
        });

        realtimeClientBuilder.AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            MoexLogSources.RealtimeRest));

        realtimeClientBuilder.AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options, moexOptions);
        })
        .Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(MoexRealtimeRestClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, MoexLogSources.RealtimeRest);
        });

        realtimeClientBuilder.AddHttpMessageHandler(sp => new MoexRateLimitHandler(
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>()));

        services.AddTransient<ProjectTraiding.Moex.Realtime.MoexWebSocketProbeClient>();

        return services;


    }

    // ══════════════════════════════════════════════
    // Общая настройка resilience (timeout, retry, circuit breaker)
    // ══════════════════════════════════════════════
    private static void ConfigureStandardResilience(
        HttpStandardResilienceOptions options,
        MoexOptions moexOptions)
    {
        // ── Timeout budget (значения по замеру, участок Г2) ──
        // TotalRequestTimeout = 5 мин (весь запрос включая все retry).
        // AttemptTimeout = 30 с (одна попытка до заголовков; тело охраняет BodyReadTimeout 30 с).
        // maxDelay Retry-After = 2 мин (ожидание перед retry при 429).
        // При таком бюджете полная серия из пяти повторов с длинной паузой не уместится —
        // управление вернётся вызывающему коду раньше. Это осознанное поведение: после
        // сужения окон нарезки (Г1) страница грузится за единицы секунд, и держать широкий
        // бюджет ради редких длинных пауз нет смысла.
        options.TotalRequestTimeout.Timeout = moexOptions.TotalRequestTimeout;
        options.AttemptTimeout.Timeout = moexOptions.AttemptTimeout;
        // Окно наблюдения размыкателя цепи (Polly CircuitBreaker) должно быть не меньше
        // удвоенного предела одной попытки — таково требование библиотеки. Берём предел
        // попытки плюс запас, чтобы одиночная медленная попытка не размыкала цепь ложно.
        options.CircuitBreaker.SamplingDuration = moexOptions.AttemptTimeout + TimeSpan.FromMinutes(3);
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

        MoexMetrics.HttpRetries.Add(
            1,
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, source),
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.ErrorType, errorType));

        return default;
    }

    
}
