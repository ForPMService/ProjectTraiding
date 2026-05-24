namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Клиентская ошибка MOEX ISS API (HTTP 4xx, кроме 400, 401, 403, 404, 429).
/// Бросается при прочих 4xx-статусах (405, 406, 409, 410, 422 и т.д.); повтор без изменений бесполезен.
/// </summary>
public sealed class MoexClientException : MoexHttpException
{
    /// <summary>
    /// Инициализирует новый экземпляр для прочих 4xx-ошибок.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 4xx.</param>
    /// <param name="statusCode">Конкретный статус-код ответа.</param>
    public MoexClientException(string endpoint, int statusCode)
        : base($"MOEX client error {statusCode} for {endpoint}")
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        IsRetryable = false;
    }

    public override string ErrorCategory => "client_error";
}
