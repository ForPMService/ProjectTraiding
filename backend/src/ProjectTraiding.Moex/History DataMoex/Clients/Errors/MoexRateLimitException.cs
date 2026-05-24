namespace History_DataMoex.Clients.Errors;

/// <summary>
/// Превышен лимит запросов к MOEX ISS API (HTTP 429 Too Many Requests).
/// Бросается, когда сервер вернул статус 429 и запрос следует повторить
/// после истечения <see cref="MoexHttpException.RetryAfter"/>.
/// </summary>
public sealed class MoexRateLimitException : MoexHttpException
{
    /// <summary>
    /// Инициализирует новый экземпляр для ошибки rate limit.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 429.</param>
    /// <param name="retryAfter">Задержка перед повторным запросом (null если не указана сервером).</param>
    public MoexRateLimitException(string endpoint, TimeSpan? retryAfter = null)
        : base($"MOEX rate limit exceeded (429) for {endpoint}")
    {
        StatusCode = 429;
        Endpoint = endpoint;
        IsRetryable = true;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Инициализирует новый экземпляр с явно заданным сообщением.
    /// Используется при прямом создании исключения с контролируемым текстом.
    /// </summary>
    /// <param name="message">Описание ошибки (не должно содержать токены или заголовки).</param>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 429.</param>
    /// <param name="retryAfter">Задержка перед повторным запросом (null если не указана сервером).</param>
    public MoexRateLimitException(string message, string endpoint, TimeSpan? retryAfter = null)
        : base(message)
    {
        StatusCode = 429;
        Endpoint = endpoint;
        IsRetryable = true;
        RetryAfter = retryAfter;
    }

    public override string ErrorCategory => "rate_limit";
}
