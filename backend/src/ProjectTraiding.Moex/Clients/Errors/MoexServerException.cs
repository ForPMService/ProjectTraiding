namespace History_DataMoex.Clients.Errors;

/// <summary>
/// Серверная ошибка MOEX ISS API (HTTP 5xx).
/// Бросается при получении статуса 500–599; запрос может быть повторён.
/// </summary>
public sealed class MoexServerException : MoexHttpException
{
    /// <summary>
    /// Инициализирует новый экземпляр для серверной ошибки 5xx.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 5xx.</param>
    /// <param name="statusCode">Конкретный код ответа (500, 502, 503, 504 и т.д.).</param>
    public MoexServerException(string endpoint, int statusCode)
        : base($"MOEX server error {statusCode} for {endpoint}")
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        IsRetryable = true;
    }

    public override string ErrorCategory => "server_error";
}
