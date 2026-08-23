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
    /// <param name="droppedActivityNamePrefixes">
    /// Начала имён отрезков, которые в трассировку не попадают. Пустой перечень оставляет
    /// поведение SDK по умолчанию — ParentBased(root=AlwaysOn), то есть сто процентов
    /// корневых отрезков.
    /// </param>
    public static WebApplicationBuilder AddProjectTraidingObservability(
        this WebApplicationBuilder builder,
        string[] activitySources,
        string[] meters,
        string[] droppedActivityNamePrefixes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(activitySources);
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(droppedActivityNamePrefixes);

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

                // Сэмплер оборачивается родительским: отброшенный отрезок должен унести
                // с собой всё поддерево, включая отрезок исходящего запроса, который
                // автоинструментация HttpClient завела бы внутри него. Без обёртки
                // отбрасывался бы только сам отрезок, а его потомки остались бы в Tempo
                // без родителя. Для всего, что не подпадает под префикс, поведение
                // совпадает с настройкой SDK по умолчанию.
                if (droppedActivityNamePrefixes.Length > 0)
                {
                    tracing.SetSampler(new ParentBasedSampler(
                        new ActivityNamePrefixSampler(droppedActivityNamePrefixes)));
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

                // Автоинструментация исходящих запросов исключена из конвейера метрик:
                // семейство http.client.* дублирует собственную гистограмму
                // moex.http.request.duration и не читается ни одной панелью.
                // В конвейере трассировки она остаётся — там от неё зависят
                // родительские отрезки исходящих обращений.
                metrics
                    .AddAspNetCoreInstrumentation()
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
        // Конкретные проверки (PostgreSQL, Collector, ClickHouse)
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

    /// <summary>
    /// Сэмплер, отбрасывающий отрезки, имя которых начинается с одного из заданных
    /// префиксов. Решение принимается только по имени и без жребия: отбрасываемое
    /// отбрасывается всегда, остальное сохраняется целиком. Прореживание по доле здесь
    /// неприменимо — оно выбросило бы ту же долю исторических загрузок, а их единицы
    /// в день и каждая нужна.
    ///
    /// Тип вложен и закрыт намеренно: у него ровно один потребитель — сборка конвейера
    /// трассировки выше в этом же файле. Отдельный публичный тип с одним потребителем
    /// добавил бы имя в пространство имён и файл, не дав ничего взамен. Класс существует
    /// только потому, что этого требует договор OpenTelemetry: пользовательская логика
    /// отбора задаётся наследником абстрактного Sampler, принимать функцию SDK не умеет.
    /// </summary>
    private sealed class ActivityNamePrefixSampler : Sampler
    {
        private readonly string[] _droppedPrefixes;

        public ActivityNamePrefixSampler(string[] droppedPrefixes)
        {
            _droppedPrefixes = droppedPrefixes;
        }

        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            for (int i = 0; i < _droppedPrefixes.Length; i++)
            {
                if (samplingParameters.Name.StartsWith(
                        _droppedPrefixes[i], StringComparison.Ordinal))
                {
                    return new SamplingResult(SamplingDecision.Drop);
                }
            }

            return new SamplingResult(SamplingDecision.RecordAndSample);
        }
    }
}
