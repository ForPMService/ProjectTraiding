using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.CustomFeatures.Clients;
using ProjectTraiding.CustomFeatures.Options;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;

namespace ProjectTraiding.CustomFeatures.Infrastructure.DependencyInjection;

public static class CustomFeaturesCalendarSourceServiceCollectionExtensions
{
    private const int MaxRetryAttempts = 5;

    private static readonly string[] ExpectedRootHashes =
    [
        "D26D2D0231B7C39F92CC738512BA54103519E4405D68B5BD703E9788CA8ECF31",
    ];

    private static readonly string[] ExpectedIntermediateHashes =
    [
        "2155785036C900DBB5F1BB2A1569C80C55595BD6BF94867A29BBDDBC7D88A3F2",
    ];

    public static IServiceCollection AddCustomFeaturesCalendarSource(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CalendarSourceOptions>()
            .Bind(configuration.GetSection("Moex"));

        CalendarSourceOptions options =
            configuration.GetSection("Moex").Get<CalendarSourceOptions>() ?? new CalendarSourceOptions();

        string certificatesDirectory =
            Path.Combine(AppContext.BaseDirectory, options.CertificatesDirectory);

        (
            X509Certificate2Collection Roots,
            X509Certificate2Collection Intermediates
        ) nucTrust = (
            LoadCertificateSet(certificatesDirectory, "Roots", ExpectedRootHashes),
            LoadCertificateSet(certificatesDirectory, "Intermediates", ExpectedIntermediateHashes));

        AddCalendarHttpClient<CalendarApimClient>(services, options, CalendarLogSources.Apim, nucTrust);
        AddCalendarHttpClient<CalendarIssClient>(services, options, CalendarLogSources.Iss, nucTrust: null);

        return services;
    }

    private static void AddCalendarHttpClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        IServiceCollection services,
        CalendarSourceOptions options,
        string logSource,
        (X509Certificate2Collection Roots, X509Certificate2Collection Intermediates)? nucTrust)
        where TClient : class
    {
        IHttpClientBuilder builder = services.AddHttpClient<TClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            CalendarSourceOptions sourceOptions = sp
                .GetRequiredService<IOptions<CalendarSourceOptions>>().Value;

            SocketsHttpHandler handler = new()
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = sourceOptions.MaxConnectionsPerServer,
            };

            if (nucTrust is not null)
            {
                X509ChainPolicy chainPolicy = new() { TrustMode = X509ChainTrustMode.CustomRootTrust };
                chainPolicy.CustomTrustStore.AddRange(nucTrust.Value.Roots);
                chainPolicy.ExtraStore.AddRange(nucTrust.Value.Intermediates);
                ApplyRevocationPolicy(chainPolicy, sourceOptions.CertificateRevocationPolicy);

                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    CertificateChainPolicy = chainPolicy,
                };
            }

            return handler;
        });

        builder.AddStandardResilienceHandler(resilienceOptions =>
        {
            ConfigureStandardResilience(resilienceOptions, options);
        })
        .Configure((resilienceOptions, sp) =>
        {
            ILogger logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{typeof(TClient).FullName}.CalendarRetryPolicy");
            resilienceOptions.Retry.OnRetry = args => OnRetryHandler(args, logger, logSource);
        });

        builder.AddHttpMessageHandler(sp => new CalendarHttpLoggingHandler(
            sp.GetRequiredService<ILogger<CalendarHttpLoggingHandler>>(),
            logSource));
    }

    private static void ConfigureStandardResilience(
        HttpStandardResilienceOptions options,
        CalendarSourceOptions sourceOptions)
    {
        options.RateLimiter.DefaultRateLimiterOptions =
            new ConcurrencyLimiterOptions
            {
                PermitLimit = 1_000,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            };

        options.TotalRequestTimeout.Timeout = sourceOptions.TotalRequestTimeout;
        options.AttemptTimeout.Timeout = sourceOptions.AttemptTimeout;
        options.CircuitBreaker.SamplingDuration = sourceOptions.AttemptTimeout + TimeSpan.FromMinutes(3);
        options.Retry.MaxRetryAttempts = MaxRetryAttempts;
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result is HttpResponseMessage { StatusCode: (HttpStatusCode)429 } resp)
            {
                TimeSpan? delay = CalendarHttpClientHelpers.GetRetryAfterForPolly(
                    resp,
                    TimeSpan.FromMinutes(2));
                if (delay is not null)
                    return ValueTask.FromResult<TimeSpan?>(delay);
            }

            return ValueTask.FromResult<TimeSpan?>(null);
        };
    }

    private static ValueTask OnRetryHandler(
        Polly.Retry.OnRetryArguments<HttpResponseMessage> args,
        ILogger logger,
        string source)
    {
        HttpStatusCode? statusCode = args.Outcome.Result?.StatusCode;
        string endpoint = args.Outcome.Result?.RequestMessage?.RequestUri?.PathAndQuery ?? "unknown";
        int? status = (int?)statusCode;

        string errorType = status switch
        {
            null when args.Outcome.Exception is TimeoutRejectedException => CalendarErrorTypes.Timeout,
            null when args.Outcome.Exception is TaskCanceledException => CalendarErrorTypes.Timeout,
            null when args.Outcome.Exception is HttpRequestException => CalendarErrorTypes.TransportError,
            null => CalendarErrorTypes.Unknown,
            _ => CalendarHttpClientHelpers.ClassifyStatus(status.Value)
        };

        CalendarHttpLogMessages.RetryAttempt(
            logger,
            source,
            endpoint,
            args.AttemptNumber + 1,
            MaxRetryAttempts,
            errorType,
            args.RetryDelay,
            statusCode);

        return default;
    }

    private static X509Certificate2Collection LoadCertificateSet(
        string certificatesDirectory,
        string subdirectory,
        string[] expectedHashes)
    {
        string directory = Path.Combine(certificatesDirectory, subdirectory);

        if (!Directory.Exists(directory))
            throw new InvalidOperationException(
                $"Каталог сертификатов не найден: {directory}. " +
                "Проверьте запись Content в файле проекта ProjectTraiding.CustomFeatures.");

        X509Certificate2Collection set = [];
        HashSet<string> actual = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(directory, "*.cer"))
        {
            byte[] bytes = File.ReadAllBytes(file);
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

    private static void ApplyRevocationPolicy(X509ChainPolicy policy, CalendarRevocationPolicy mode)
    {
        switch (mode)
        {
            case CalendarRevocationPolicy.Off:
                policy.RevocationMode = X509RevocationMode.NoCheck;
                policy.VerificationFlags = X509VerificationFlags.NoFlag;
                break;

            case CalendarRevocationPolicy.SoftFail:
                policy.RevocationMode = X509RevocationMode.Online;
                policy.VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown
                                         | X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown;
                break;

            case CalendarRevocationPolicy.Strict:
                policy.RevocationMode = X509RevocationMode.Online;
                policy.VerificationFlags = X509VerificationFlags.NoFlag;
                break;

            default:
                throw new InvalidOperationException($"Неизвестный режим проверки отзыва: {mode}.");
        }
    }
}
