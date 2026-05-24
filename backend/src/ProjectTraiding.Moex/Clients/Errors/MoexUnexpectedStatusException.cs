namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Неожиданный non-2xx статус от MOEX ISS API (3xx и прочие не-4xx/5xx).
/// Бросается когда сервер вернул статус, не попадающий ни в одну из
/// специализированных категорий (например, неожиданный редирект).
/// </summary>
public sealed class MoexUnexpectedStatusException : MoexHttpException
{
    /// <summary>
    /// Инициализирует новый экземпляр для неожиданного статуса.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего неожиданный статус.</param>
    /// <param name="statusCode">Полученный статус-код ответа.</param>
    public MoexUnexpectedStatusException(string endpoint, int statusCode)
        : base($"MOEX unexpected status {statusCode} for {endpoint}")
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        IsRetryable = false;
    }

    public override string ErrorCategory => "unexpected_status";
}
