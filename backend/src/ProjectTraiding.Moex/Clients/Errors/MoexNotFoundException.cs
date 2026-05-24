namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Запрошенный ресурс не найден в MOEX ISS API (HTTP 404 Not Found).
/// Бросается когда инструмент, контракт или иной ресурс отсутствует на сервере.
/// </summary>
public sealed class MoexNotFoundException : MoexHttpException
{
    /// <summary>
    /// Инициализирует новый экземпляр для ошибки 404.
    /// </summary>
    /// <param name="endpoint">Адрес эндпоинта, вернувшего 404.</param>
    public MoexNotFoundException(string endpoint)
        : base($"MOEX resource not found (404) for {endpoint}")
    {
        StatusCode = 404;
        Endpoint = endpoint;
        IsRetryable = false;
    }

    public override string ErrorCategory => "not_found";
}
