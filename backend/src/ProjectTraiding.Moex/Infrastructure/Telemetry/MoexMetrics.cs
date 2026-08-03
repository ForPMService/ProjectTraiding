using ProjectTraiding.Moex.Errors;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Метрики контура MOEX. Каталог: «Наблюдаемость — план закрытия фазы 2 v0.3», раздел 3.
///
/// Используются в местах действия:
///   MoexHttpLoggingHandler — HTTP-запросы к MOEX.
///   MoexRateLimitHandler   — rate limiter.
///   MOEX-клиенты           — пагинация (страницы, строки).
///
/// Атрибуты метрик: MoexTelemetryAttributes.Source, .DataKind, .StatusCode, .ErrorType.
/// Не полный endpoint — правила кардинальности (план, раздел 2.7).
/// </summary>
public static class MoexMetrics
{
    private static readonly Meter Meter = new(MoexTelemetry.MeterName);

    // ══════════════════════════════════════════════
    // HTTP-запросы к MOEX
    // ══════════════════════════════════════════════

    /// <summary>Количество HTTP-запросов к MOEX.</summary>
    public static readonly Counter<long> HttpRequests =
        Meter.CreateCounter<long>("moex.http.requests", description: "MOEX HTTP requests total");

    /// <summary>Длительность HTTP-запроса (мс).</summary>
    public static readonly Histogram<double> HttpRequestDuration =
        Meter.CreateHistogram<double>("moex.http.request.duration", unit: "ms", description: "MOEX HTTP request duration");

    /// <summary>Количество ошибок HTTP (5xx, timeout, transport).</summary>
    public static readonly Counter<long> HttpErrors =
        Meter.CreateCounter<long>("moex.http.errors", description: "MOEX HTTP errors total");

    /// <summary>Количество повторных попыток (Polly retry).</summary>
    public static readonly Counter<long> HttpRetries =
        Meter.CreateCounter<long>("moex.http.retries", description: "MOEX HTTP retries total");

    // ══════════════════════════════════════════════
    // Rate limiter
    // ══════════════════════════════════════════════

    /// <summary>Количество выданных permit-ов.</summary>
    public static readonly Counter<long> RateLimitAcquired =
        Meter.CreateCounter<long>("moex.ratelimit.acquired", description: "MOEX rate limit permits acquired");

    /// <summary>Время ожидания permit (мс).</summary>
    public static readonly Histogram<double> RateLimitWaitDuration =
        Meter.CreateHistogram<double>("moex.ratelimit.wait.duration", unit: "ms", description: "MOEX rate limit wait duration");

    /// <summary>Количество запросов, ждавших дольше порога.</summary>
    public static readonly Counter<long> RateLimitQueued =
        Meter.CreateCounter<long>("moex.ratelimit.queued", description: "MOEX rate limit queued requests");

    // ══════════════════════════════════════════════
    // Пагинация
    // ══════════════════════════════════════════════

    /// <summary>Количество загруженных страниц.</summary>
    public static readonly Counter<long> PagesTotal =
        Meter.CreateCounter<long>("moex.pages.total", description: "MOEX pages loaded total");

    /// <summary>Количество загруженных строк.</summary>
    public static readonly Counter<long> RowsTotal =
        Meter.CreateCounter<long>("moex.rows.total", description: "MOEX rows loaded total");

    // ══════════════════════════════════════════════
    // Производственные операции источника
    // ══════════════════════════════════════════════

    /// <summary>
    /// Длительность одной производственной операции источника, в секундах.
    /// Границы заданы явно: умолчание библиотеки рассчитано на миллисекунды
    /// и для секундной шкалы даёт непригодное разрешение.
    ///
    /// Верхняя граница доведена до пяти минут — это полный бюджет одного запроса
    /// вместе со всеми повторами и ожиданиями между ними. Более низкий потолок
    /// сваливал бы в переполнение именно те наблюдения, ради которых метрику
    /// и смотрят: длительные операции в период повторов и деградации источника.
    /// </summary>
    public static readonly Histogram<double> OperationDuration =
        Meter.CreateHistogram<double>(
            "moex.operation.duration",
            unit: "s",
            description: "MOEX source operation duration",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = new double[]
                {
                    0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 180, 300
                }
            });

    /// <summary>Количество завершённых производственных операций источника.</summary>
    public static readonly Counter<long> OperationsCompleted =
        Meter.CreateCounter<long>(
            "moex.operations.completed",
            description: "MOEX source operations completed total");

    /// <summary>
    /// Записывает успешный исход операции. Пустой, но корректно разобранный ответ —
    /// успех: отсутствие данных за период является нормальным состоянием источника.
    /// </summary>
    public static void RecordOperationSuccess(in MoexOperationTags tags, double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Success, null, seconds);
    }

    /// <summary>
    /// Записывает отмену вызывающей стороны. Категория ошибки не добавляется:
    /// отмена не является отказом источника.
    /// </summary>
    public static void RecordOperationCancelled(in MoexOperationTags tags, double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Cancelled, null, seconds);
    }

    /// <summary>
    /// Записывает отказ операции, определяя категорию по типу исключения.
    /// Классификация выполняется здесь, а не в вызывающем коде, чтобы одно
    /// исключение везде получало одну и ту же категорию.
    /// </summary>
    public static void RecordOperationError(
        in MoexOperationTags tags,
        Exception exception,
        double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Error, Classify(exception), seconds);
    }

    /// <summary>
    /// Записывает отказ по истечению собственного бюджета ожидания. Отдельный метод
    /// нужен потому, что такое истечение приходит как отмена связанного токена и по
    /// типу исключения неотличимо от остановки хоста: различие известно только
    /// вызывающему коду, который и владеет обоими токенами.
    /// </summary>
    public static void RecordOperationTimeout(in MoexOperationTags tags, double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Error, MoexErrorTypes.Timeout, seconds);
    }

    private static void RecordOperation(
        in MoexOperationTags tags,
        string outcome,
        string? errorType,
        double seconds)
    {
        // Категория ошибки добавляется только при отказе. Пустая строка вместо
        // отсутствующей метки создала бы отдельный ряд с бессмысленным значением.
        if (errorType is null)
        {
            TagList tagList = new TagList
            {
                { MoexTelemetryAttributes.Source, tags.Source },
                { MoexTelemetryAttributes.Operation, tags.Operation },
                { MoexTelemetryAttributes.DataKind, tags.DataKind },
                { MoexTelemetryAttributes.Market, tags.Market },
                { MoexTelemetryAttributes.Outcome, outcome },
            };

            OperationsCompleted.Add(1, tagList);
            OperationDuration.Record(seconds, tagList);
            return;
        }

        TagList errorTags = new TagList
        {
            { MoexTelemetryAttributes.Source, tags.Source },
            { MoexTelemetryAttributes.Operation, tags.Operation },
            { MoexTelemetryAttributes.DataKind, tags.DataKind },
            { MoexTelemetryAttributes.Market, tags.Market },
            { MoexTelemetryAttributes.Outcome, outcome },
            { MoexTelemetryAttributes.ErrorType, errorType },
        };

        OperationsCompleted.Add(1, errorTags);
        OperationDuration.Record(seconds, errorTags);
    }

    /// <summary>
    /// Определяет категорию ошибки по типу исключения. Типизированные ошибки контура
    /// несут категорию сами; для остальных применяется узкий разбор по типу.
    ///
    /// Голый тайм-аут после первого блока из производственного чтения тела не выходит:
    /// бюджет чтения преобразует его в типизированную ошибку в месте возникновения.
    /// Ветвь оставлена как страховка — её срабатывание означает нарушение контракта
    /// где-то ещё и требует разбирательства, а не расширения этой классификации.
    /// </summary>
    private static string Classify(Exception exception)
    {
        if (exception is MoexException moexException)
            return moexException.ErrorCategory;

        if (exception is HttpRequestException)
            return MoexErrorTypes.TransportError;

        if (exception is TimeoutException)
            return MoexErrorTypes.Timeout;

        return MoexErrorTypes.Unknown;
    }

    // ══════════════════════════════════════════════
    // Исторические задания загрузки
    // ══════════════════════════════════════════════

    /// <summary>
    /// Длительность исторического задания, в секундах. Границы покрывают всю
    /// наблюдаемую шкалу: от короткого дозагрузочного задания в десятки секунд
    /// до многочасовой загрузки широкого диапазона.
    /// </summary>
    public static readonly Histogram<double> LoadTaskDuration =
        Meter.CreateHistogram<double>(
            "moex.load.task.duration",
            unit: "s",
            description: "Historical load task duration",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = new double[]
                {
                    5, 15, 30, 60, 180, 300, 900, 1800, 3600, 7200, 14400
                }
            });

    /// <summary>Количество завершённых исторических заданий.</summary>
    public static readonly Counter<long> LoadTasksCompleted =
        Meter.CreateCounter<long>(
            "moex.load.tasks.completed",
            description: "Historical load tasks completed total");

    /// <summary>
    /// Текущее число заданий в работе. Счётчик двусторонний: увеличивается при
    /// фактическом получении владения задачей и уменьшается при выходе из
    /// защищённого жизненного цикла.
    /// </summary>
    public static readonly UpDownCounter<long> LoadTasksActive =
        Meter.CreateUpDownCounter<long>(
            "moex.load.tasks.active",
            description: "Historical load tasks currently running");
}
