namespace History_DataMoex.Clients.Errors;

/// <summary>
/// Базовый абстрактный класс для всех HTTP-ошибок взаимодействия с MOEX ISS API.
/// Не содержит заголовков запроса/ответа, Bearer-токенов или иных чувствительных данных.
/// </summary>
public abstract class MoexHttpException : Exception
{
    /// <summary>HTTP статус-код ответа (null для timeout или сетевых ошибок).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Адрес эндпоинта, вызов которого привёл к ошибке.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Код источника ошибки (например, имя метода или операции).</summary>
    public string? SourceCode { get; init; }

    /// <summary>Идентификатор корреляции запроса для трассировки.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Признак того, что запрос можно повторить после ошибки.</summary>
    public bool IsRetryable { get; init; }

    /// <summary>Рекомендуемая задержка перед повторным запросом (null если не указана).</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Категория ошибки для структурированного логирования.
    /// Возвращает стабильную строку-константу (например, "rate_limit", "server_error").
    /// Не зависит от StatusCode и не меняется между версиями.
    /// </summary>
    public abstract string ErrorCategory { get; }

    /// <summary>
    /// Инициализирует новый экземпляр с сообщением и опциональным внутренним исключением.
    /// </summary>
    /// <param name="message">Описание ошибки. Не должно содержать токены или заголовки.</param>
    /// <param name="inner">Исходное исключение (например, <see cref="TaskCanceledException"/>).</param>
    protected MoexHttpException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
