using System.Diagnostics.Metrics;

namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Метрики контура MOEX. Каталог: «Наблюдаемость — план закрытия фазы 2 v0.3», раздел 3.
///
/// Используются в местах действия:
///   MoexHttpLoggingHandler — HTTP-запросы к MOEX.
///   MoexRateLimitHandler   — rate limiter.
///   MoexRawCaptureWriter   — raw-capture в S3.
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
    // Raw-capture (S3)
    // ══════════════════════════════════════════════

    /// <summary>Количество записей в S3.</summary>
    public static readonly Counter<long> RawCaptureWrites =
        Meter.CreateCounter<long>("moex.rawcapture.writes", description: "MOEX raw capture writes total");

    /// <summary>Количество ошибок записи в S3.</summary>
    public static readonly Counter<long> RawCaptureErrors =
        Meter.CreateCounter<long>("moex.rawcapture.errors", description: "MOEX raw capture errors total");

    /// <summary>Объём записанных байтов в S3.</summary>
    public static readonly Counter<long> RawCaptureBytes =
        Meter.CreateCounter<long>("moex.rawcapture.bytes", unit: "By", description: "MOEX raw capture bytes written");
}
