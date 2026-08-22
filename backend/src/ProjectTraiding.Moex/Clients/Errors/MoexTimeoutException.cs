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
    /// <param name="inner">Исходное исключение таймаута (например, <see cref="TaskCanceledException"/>).</param>
    public MoexTimeoutException(string message, string endpoint, string timeoutSource, Exception? inner = null)
        : base(message, inner)
    {
        Endpoint = endpoint;
        TimeoutSource = timeoutSource;
    }

    /// <summary>
    /// Инициализирует экземпляр для ответа со статусом 408 Request Timeout.
    /// Источником тайм-аута объявлен сам ответ Московской биржи, а не сторож
    /// времени попытки или чтения тела, поэтому TimeoutSource — "http_status",
    /// а StatusCode заполнен фактическим кодом ответа.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 408.</param>
    public MoexTimeoutException(string endpoint)
        : base($"MOEX request timeout (408) for {endpoint}")
    {
        StatusCode = 408;
        Endpoint = endpoint;
        TimeoutSource = "http_status";
    }

    public override string ErrorCategory => MoexErrorTypes.Timeout;
}
