using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ProjectTraiding.Observability.Infrastructure.DependencyInjection;

/// <summary>
/// Хостовый инфраструктурный модуль настройки наблюдаемости.
/// Не является библиотекой предметного модуля.
/// AddOpenTelemetry() вызывается только через ProjectTraiding.Api при сборке приложения,
/// в соответствии с рекомендацией OpenTelemetry SDK (host code, не library code).
///
/// Модуль настраивает «трубу» OpenTelemetry. Предметные модули (Moex, Storage, ...)
/// кладут в неё свои события, метрики и трейсы через стандартные BCL-типы:
/// System.Diagnostics.ActivitySource и System.Diagnostics.Metrics.Meter.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    private const string DefaultServiceName = "ProjectTraiding.Api";

    /// <summary>
    /// Настраивает OpenTelemetry: tracing, metrics, logging через OTLP exporter.
    /// Подключает базовый механизм health checks (без конкретных проверок зависимостей).
    /// </summary>
    /// <param name="builder">WebApplicationBuilder приложения.</param>
    /// <param name="activitySources">Имена ActivitySource предметных модулей для подписки tracing.</param>
    /// <param name="meters">Имена Meter предметных модулей для подписки metrics.</param>
    public static WebApplicationBuilder AddProjectTraidingObservability(
        this WebApplicationBuilder builder,
        string[] activitySources,
        string[] meters)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(activitySources);
        ArgumentNullException.ThrowIfNull(meters);

        // ══════════════════════════════════════════════
        // Service name: OTEL_SERVICE_NAME → default
        // ══════════════════════════════════════════════
        string? configuredServiceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");

        string serviceName = string.IsNullOrWhiteSpace(configuredServiceName)
            ? DefaultServiceName
            : configuredServiceName;

        // ══════════════════════════════════════════════
        // Tracing + Metrics
        // ══════════════════════════════════════════════
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName: serviceName))
            .WithTracing(tracing =>
            {
                foreach (string source in activitySources)
                {
                    tracing.AddSource(source);
                }

                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                foreach (string meter in meters)
                {
                    metrics.AddMeter(meter);
                }

                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter();
            });

        // ══════════════════════════════════════════════
        // Logging — отдельно от tracing/metrics.
        // trace_id и span_id попадают в OTLP logs из активного Activity автоматически.
        // IncludeScopes передаёт ILogger scopes, если они появятся позже.
        // IncludeFormattedMessage — читаемое сообщение в OTLP, а не только шаблон.
        // SetResourceBuilder — чтобы logs/traces/metrics имели один service.name.
        // OTLP endpoint: OTEL_EXPORTER_OTLP_ENDPOINT (SDK читает env var автоматически).
        // ══════════════════════════════════════════════
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.SetResourceBuilder(
                ResourceBuilder.CreateDefault().AddService(serviceName));
            logging.AddOtlpExporter();
        });

        // ══════════════════════════════════════════════
        // Health checks — базовый механизм.
        // Конкретные проверки (PostgreSQL, Collector, ClickHouse, Redis)
        // добавляются в хосте или в модулях, которые владеют этими зависимостями.
        // ══════════════════════════════════════════════
        builder.Services.AddHealthChecks();

        return builder;
    }

    /// <summary>
    /// Мапит endpoint-ы наблюдаемости: /healthz.
    /// Вызывается после builder.Build().
    /// </summary>
    public static WebApplication MapObservabilityEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/healthz");
        return app;
    }
}
