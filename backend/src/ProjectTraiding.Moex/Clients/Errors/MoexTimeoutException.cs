namespace History_DataMoex.Clients.Errors;

/// <summary>
/// Истекло время ожидания ответа от MOEX ISS API.
/// Бросается при <see cref="TaskCanceledException"/> или <see cref="TimeoutException"/>
/// во время HTTP-запроса; запрос может быть повторён.
/// </summary>
public sealed class MoexTimeoutException : MoexHttpException
{
    /// <summary>Настроенный таймаут запроса (null если не передан явно).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Источник таймаута: "http_client" (HttpClient.Timeout / TaskCanceledException)
    /// или "polly_attempt" (Polly AttemptTimeout / TimeoutRejectedException).
    /// </summary>
    public string TimeoutSource { get; }

    /// <summary>
    /// Инициализирует новый экземпляр для ошибки таймаута.
    /// </summary>
    /// <param name="message">Описание ошибки (например, «request timeout» vs «attempt timeout»).</param>
    /// <param name="endpoint">Адрес эндпоинта, превысившего таймаут.</param>
    /// <param name="timeoutSource">Источник таймаута: "http_client" или "polly_attempt".</param>
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

    public override string ErrorCategory => "timeout";
}
