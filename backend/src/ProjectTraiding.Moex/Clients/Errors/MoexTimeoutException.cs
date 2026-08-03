using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Истекло время ожидания взаимодействия с MOEX ISS API.
/// Бросается при тайм-ауте транспорта, попытки слоя устойчивости, чтения тела
/// ответа или при ответе источника со статусом 408; запрос может быть повторён.
/// </summary>
public sealed class MoexTimeoutException : MoexHttpException
{
    /// <summary>
    /// Настроенный тайм-аут запроса или чтения тела. При статусе 408 равен null,
    /// поскольку настроенного нами бюджета в этом случае нет.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Источник тайм-аута:
    /// "http_client" — истёк тайм-аут транспортного запроса;
    /// "polly_attempt" — истёк предел одной попытки слоя устойчивости;
    /// "body_read" — истёк бюджет чтения тела ответа;
    /// "http_status" — сам источник ответил статусом 408.
    /// </summary>
    public string TimeoutSource { get; }

    /// <summary>
    /// Инициализирует новый экземпляр для ошибки таймаута.
    /// </summary>
    /// <param name="message">Описание ошибки (например, «request timeout» vs «attempt timeout»).</param>
    /// <param name="endpoint">Адрес эндпоинта, превысившего таймаут.</param>
    /// <param name="timeoutSource">
    /// Источник тайм-аута: "http_client", "polly_attempt", "body_read" или "http_status".
    /// </param>
    /// <param name="timeout">Настроенное время ожидания запроса (null если неизвестно).</param>
    /// <param name="inner">Исходное исключение таймаута (например, <see cref="TaskCanceledException"/>).</param>
    public MoexTimeoutException(string message, string endpoint, string timeoutSource, TimeSpan? timeout = null, Exception? inner = null)
        : base(message, inner)
    {
        Endpoint = endpoint;
        IsRetryable = true;
        Timeout = timeout;
        TimeoutSource = timeoutSource;
    }

    /// <summary>
    /// Инициализирует экземпляр для ответа со статусом 408 Request Timeout.
    /// Источником тайм-аута объявлен сам ответ Московской биржи, а не сторож
    /// времени попытки или чтения тела, поэтому TimeoutSource — "http_status",
    /// а StatusCode заполнен фактическим кодом ответа.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 408.</param>
    /// <param name="retryAfter">Задержка перед повтором (null если не указана сервером).</param>
    public MoexTimeoutException(string endpoint, TimeSpan? retryAfter = null)
        : base($"MOEX request timeout (408) for {endpoint}")
    {
        StatusCode = 408;
        Endpoint = endpoint;
        IsRetryable = true;
        RetryAfter = retryAfter;
        TimeoutSource = "http_status";
    }

    public override string ErrorCategory => MoexErrorTypes.Timeout;
}
