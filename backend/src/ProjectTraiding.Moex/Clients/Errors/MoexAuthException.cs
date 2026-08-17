using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Ошибка аутентификации или авторизации в MOEX ISS API (HTTP 401 или 403).
/// Бросается при отсутствии прав доступа или истёкшем токене; повтор бесполезен.
/// </summary>
public sealed class MoexAuthException : MoexHttpException
{
    /// <summary>
    /// Инициализирует новый экземпляр для ошибки 401/403.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 401 или 403.</param>
    /// <param name="statusCode">Конкретный статус-код (401 или 403).</param>
    public MoexAuthException(string endpoint, int statusCode)
        : base($"MOEX authentication/authorization error {statusCode} for {endpoint}")
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        IsRetryable = false;
    }

    public override string ErrorCategory => MoexErrorTypes.Auth;
}
