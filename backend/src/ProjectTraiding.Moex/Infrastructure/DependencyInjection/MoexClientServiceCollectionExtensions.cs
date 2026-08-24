using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.RateLimiting;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection;

public static class MoexClientServiceCollectionExtensions
{
    private const int MaxRetryAttempts = 5;
    /// <summary>
    /// Отпечатки SHA-256 сертификатов НУЦ Минцифры. Значения не секретны; плановая смена
    /// сертификата — сознательная правка кода, а не изменение настройки. Ожидаемый состав
    /// каталога сверяется с этими наборами как множество с множеством.
    /// </summary>
    private static readonly string[] ExpectedRootHashes =
    [
        // Russian Trusted Root CA, 02.03.2022 — 28.02.2032
        "D26D2D0231B7C39F92CC738512BA54103519E4405D68B5BD703E9788CA8ECF31",
    ];

    private static readonly string[] ExpectedIntermediateHashes =
   [
       // Russian Trusted Sub CA, 15.07.2024 — 19.07.2029
       "2155785036C900DBB5F1BB2A1569C80C55595BD6BF94867A29BBDDBC7D88A3F2",
    ];
    public static IServiceCollection AddMoexClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        MoexMetrics.EnsureInitialized();

        services.AddOptions<MoexOptions>()
            .Bind(configuration.GetSection("Moex"));
        MoexOptions moexOptions = configuration.GetSection("Moex").Get<MoexOptions>() ?? new MoexOptions();
        string certificatesDirectory = Path.Combine(AppContext.BaseDirectory, moexOptions.CertificatesDirectory);
        (
            X509Certificate2Collection Roots,
            X509Certificate2Collection Intermediates
        ) nucTrust = (
            LoadCertificateSet(certificatesDirectory, "Roots", ExpectedRootHashes),
            LoadCertificateSet(certificatesDirectory, "Intermediates", ExpectedIntermediateHashes));
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

        AddMoexHttpClient<MoexHttpIssClient>(services, moexOptions, MoexLogSources.Iss, nucTrust: null);
        AddMoexHttpClient<MoexHttpAlgClient>(services, moexOptions, MoexLogSources.Algopack, nucTrust);
        AddMoexHttpClient<MoexHttpCalendarClient>(services, moexOptions, MoexLogSources.Calendar, nucTrust);
        AddMoexHttpClient<MoexRealtimeRestClient>(services, moexOptions, MoexLogSources.RealtimeRest, nucTrust);

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
        string logSource,
        (X509Certificate2Collection Roots, X509Certificate2Collection Intermediates)? nucTrust)
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

            SocketsHttpHandler handler = new()
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            };

            // Корень НУЦ Минцифры доверяется только тем клиентам, чьи адреса на него перешли.
            // CustomRootTrust заменяет системный набор корней целиком, поэтому клиенты без
            // этой политики сохраняют штатное системное доверие без изменений.
            if (nucTrust is not null)
            {
                X509ChainPolicy chainPolicy = new() { TrustMode = X509ChainTrustMode.CustomRootTrust };
                chainPolicy.CustomTrustStore.AddRange(nucTrust.Value.Roots);
                // Выпускающие сертификаты не становятся доверенными корнями — они лишь дают
                // цепочке материал для достройки, если сервер не прислал их в рукопожатии.
                chainPolicy.ExtraStore.AddRange(nucTrust.Value.Intermediates);
                ApplyRevocationPolicy(chainPolicy, options.CertificateRevocationPolicy);

                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    CertificateChainPolicy = chainPolicy,
                };
            }

            return handler;
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
            null when args.Outcome.Exception is TimeoutRejectedException => MoexErrorTypes.Timeout,
            null when args.Outcome.Exception is TaskCanceledException => MoexErrorTypes.Timeout,
            null when args.Outcome.Exception is HttpRequestException => MoexErrorTypes.TransportError,
            null => MoexErrorTypes.Unknown,
            _ => HttpClientHelpers.ClassifyStatus(status.Value)
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

    /// <summary>
    /// Читает один набор сертификатов НУЦ из подкаталога и сверяет его состав с ожидаемым.
    /// Загруженные объекты живут до остановки приложения — владелец с определённым временем
    /// жизни есть, поэтому using здесь неуместен.
    /// Сверка идёт как множество с множеством: дубликат файла или отсутствие ожидаемого
    /// сертификата останавливают запуск. Проверки только по количеству недостаточно —
    /// две копии одного корня прошли бы её, а второго корня не было бы.
    /// </summary>
    private static X509Certificate2Collection LoadCertificateSet(
        string certificatesDirectory,
        string subdirectory,
        string[] expectedHashes)
    {
        string directory = Path.Combine(certificatesDirectory, subdirectory);

        if (!Directory.Exists(directory))
            throw new InvalidOperationException(
                $"Каталог сертификатов не найден: {directory}. " +
                "Проверьте запись Content в файле проекта ProjectTraiding.Moex.");

        X509Certificate2Collection set = [];
        HashSet<string> actual = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(directory, "*.cer"))
        {
            byte[] bytes = File.ReadAllBytes(file);

            // Файлы с портала Госуслуг встречаются и текстовыми, и двоичными.
            X509Certificate2 certificate = bytes.AsSpan().StartsWith("-----BEGIN"u8)
                ? X509Certificate2.CreateFromPem(File.ReadAllText(file))
                : X509CertificateLoader.LoadCertificate(bytes);

            string hash = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

            if (!actual.Add(hash))
                throw new InvalidOperationException(
                    $"Дубликат сертификата в подкаталоге {subdirectory}: " +
                    $"{Path.GetFileName(file)}, отпечаток {hash}.");

            set.Add(certificate);
        }

        HashSet<string> expected = new(expectedHashes, StringComparer.OrdinalIgnoreCase);

        if (!actual.SetEquals(expected))
        {
            string missing = string.Join(", ", expected.Except(actual));
            string extra = string.Join(", ", actual.Except(expected));
            throw new InvalidOperationException(
                $"Состав сертификатов в подкаталоге {subdirectory} не совпадает с ожидаемым. " +
                $"Отсутствуют: [{missing}]. Не ожидались: [{extra}].");
        }

        return set;
    }

    /// <summary>
    /// Переводит политику проверки отзыва из настройки в состояние цепочки.
    /// Каждая ветвь задаёт оба поля целиком, чтобы результат не зависел от того,
    /// была ли политика создана заново.
    /// </summary>
    private static void ApplyRevocationPolicy(X509ChainPolicy policy, MoexRevocationPolicy mode)
    {
        switch (mode)
        {
            case MoexRevocationPolicy.Off:
                policy.RevocationMode = X509RevocationMode.NoCheck;
                policy.VerificationFlags = X509VerificationFlags.NoFlag;
                break;

            case MoexRevocationPolicy.SoftFail:
                policy.RevocationMode = X509RevocationMode.Online;
                // Корень исключён из проверки отзыва штатно (RevocationFlag = ExcludeRoot),
                // поэтому флаг для корня не нужен.
                policy.VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown
                                         | X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown;
                break;

            case MoexRevocationPolicy.Strict:
                policy.RevocationMode = X509RevocationMode.Online;
                policy.VerificationFlags = X509VerificationFlags.NoFlag;
                break;

            default:
                throw new InvalidOperationException($"Неизвестный режим проверки отзыва: {mode}.");
        }
    }

}
