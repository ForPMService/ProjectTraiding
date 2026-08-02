namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Общие имена атрибутов телеметрии для логов, трейсов и метрик.
/// Каталог атрибутов: «Наблюдаемость — план закрытия фазы 2 v0.3», раздел 2.5.
///
/// Временно живут в ProjectTraiding.Moex, потому что Moex не ссылается
/// на ProjectTraiding.Observability. При появлении второго предметного модуля
/// (Storage, DataQuality) — выносятся в ProjectTraiding.Telemetry.Abstractions.
/// </summary>
public static class MoexTelemetryAttributes
{
    /// <summary>Источник: MOEX_ISS, MOEX_ALGOPACK, MOEX_CALENDAR, MOEX_REALTIME_REST.</summary>
    public const string Source = "source";

    /// <summary>Тип данных: candles, tradestats, obstats, orderstats, futoi, hi2, alerts.
    /// Обязателен для рыночных данных. Для справочников и календаря допускается отсутствие.</summary>
    public const string DataKind = "data_kind";

    /// <summary>Инструмент: SBER, SiM6.</summary>
    public const string Secid = "secid";

    /// <summary>Рынок: stock, futures.</summary>
    public const string Market = "market";

    /// <summary>Нормализованный шаблон endpoint-а для метрик (не полный PathAndQuery).
    /// В логах допустим полный endpoint; в метриках — только шаблон (кардинальность).</summary>
    public const string EndpointTemplate = "endpoint_template";

    /// <summary>
    /// Категория ошибки. Стабильные значения определены в
    /// <see cref="MoexErrorTypes"/>.
    /// </summary>
    public const string ErrorType = "error_type";

    /// <summary>HTTP status code.</summary>
    public const string StatusCode = "status_code";
}
