using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using System.Diagnostics.CodeAnalysis;
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

        AddMoexHttpClient<MoexHttpIssClient>(services, moexOptions, MoexLogSources.Iss);
        AddMoexHttpClient<MoexHttpAlgClient>(services, moexOptions, MoexLogSources.Algopack);
        AddMoexHttpClient<MoexHttpCalendarClient>(services, moexOptions, MoexLogSources.Calendar);
        AddMoexHttpClient<MoexRealtimeRestClient>(services, moexOptions, MoexLogSources.RealtimeRest);

        return services;


    }

    /// <summary>
    /// Регистрация одного клиента Московской биржи: транспорт и общий конвейер обработчиков.
    /// Порядок от внешнего к транспорту: устойчивость → общий ограничитель источника →
    /// журналирование → сокет. Устойчивость сверху, потому что она владеет операцией целиком;
    /// ограничитель под ней, потому что жетон обязана тратить каждая фактическая попытка, а не
    /// логическая операция; журналирование под ограничителем, потому что в журнал должна попадать
    /// только та попытка, которая допущена ограничителем и передана транспорту.
    /// Обобщение закрывается четырьмя известными типами клиентов — отражения нет.
    /// </summary>
    private static void AddMoexHttpClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        IServiceCollection services,
        MoexOptions moexOptions,
        string logSource)
        where TClient : class
    {
        IHttpClientBuilder builder = services.AddHttpClient<TClient>(client =>
        {
            // Общий предел клиента снят намеренно: бюджетами владеет слой устойчивости
            // (TotalRequestTimeout и AttemptTimeout), фазу чтения тела охраняет BodyReadTimeout.
            client.Timeout = Timeout.InfiniteTimeSpan;
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
        });

        builder.AddStandardResilienceHandler(options =>
        {
            ConfigureStandardResilience(options, moexOptions);
        })
        .Configure((options, sp) =>
        {
            ILogger logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(TClient).FullName}.MoexRetryPolicy");
            options.Retry.OnRetry = args => OnRetryHandler(args, logger, logSource);
        });

        builder.AddHttpMessageHandler(sp => new MoexRateLimitHandler(
            sp.GetRequiredService<RateLimiter>(),
            sp.GetRequiredService<IOptions<MoexOptions>>().Value,
            sp.GetRequiredService<ILogger<MoexRateLimitHandler>>(),
            logSource));

        builder.AddHttpMessageHandler(sp => new MoexHttpLoggingHandler(
            sp.GetRequiredService<ILogger<MoexHttpLoggingHandler>>(),
            logSource));
    }

    // ══════════════════════════════════════════════
    // Общая настройка resilience (timeout, retry, circuit breaker)
    // ══════════════════════════════════════════════
    private static void ConfigureStandardResilience(
        HttpStandardResilienceOptions options,
        MoexOptions moexOptions)
    {
        // Встроенный ограничитель стандартного конвейера ограничивает конкурентность, а не
        // частоту. Он свой у каждого клиента, стоит выше повторов и выдаёт разрешение один раз
        // на логическую операцию. Частоту обращений к MOEX держит общий TokenBucketRateLimiter
        // ниже слоя устойчивости: он выдаёт жетон на каждую фактическую попытку.
        // Значения совпадают с умолчаниями Microsoft.Extensions.Http.Resilience 10.8.0;
        // фиксируем их явно, не меняя прежнее поведение.
        options.RateLimiter.DefaultRateLimiterOptions =
            new ConcurrencyLimiterOptions
            {
                PermitLimit = 1_000,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            };

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

        // Числовое представление нужно для диапазона серверных ответов: перечисление
        // отдельных кодов пропускало 501, 505 и прочие 5xx в ветвь "unknown".
        int? status = (int?)statusCode;

        string errorType = status switch
        {
            429 => MoexErrorTypes.RateLimit,
            408 => MoexErrorTypes.Timeout,
            >= 500 and <= 599 => MoexErrorTypes.ServerError,
            null when args.Outcome.Exception is TimeoutRejectedException => MoexErrorTypes.Timeout,
            null when args.Outcome.Exception is TaskCanceledException => MoexErrorTypes.Timeout,
            null when args.Outcome.Exception is HttpRequestException => MoexErrorTypes.TransportError,
            _ => MoexErrorTypes.Unknown
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
