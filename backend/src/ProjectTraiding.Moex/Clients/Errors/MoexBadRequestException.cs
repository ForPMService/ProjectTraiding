using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Некорректный запрос к MOEX ISS API (HTTP 400 Bad Request).
/// Бросается при ошибках валидации параметров на стороне сервера; повтор без изменений бесполезен.
/// </summary>
public sealed class MoexBadRequestException : MoexHttpException
{
    /// <summary>
    /// Инициализирует новый экземпляр для ошибки 400.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 400.</param>
    public MoexBadRequestException(string endpoint)
        : base($"MOEX bad request (400) for {endpoint}")
    {
        StatusCode = 400;
        Endpoint = endpoint;
        IsRetryable = false;
    }

    public override string ErrorCategory => MoexErrorTypes.BadRequest;
}
