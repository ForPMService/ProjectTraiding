namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Стабильные категории ошибок контура MOEX для исключений,
/// журналов и метрик.
/// Значение категории не зависит от места, где ошибка была зарегистрирована.
/// </summary>
public static class MoexErrorTypes
{
    public const string RateLimit = "rate_limit";
    public const string Auth = "auth";
    public const string BadRequest = "bad_request";
    public const string NotFound = "not_found";
    public const string ServerError = "server_error";
    public const string ClientError = "client_error";
    public const string UnexpectedStatus = "unexpected_status";
    public const string Timeout = "timeout";
    public const string RateLimitRejected = "rate_limit_rejected";
    public const string SchemaMismatch = "schema_mismatch";
    public const string TransportError = "transport_error";
    public const string Unknown = "unknown";
}
