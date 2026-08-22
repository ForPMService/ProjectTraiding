namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Ошибка HTTP-обращения к бирже: ответ пришёл с кодом, отличным от успешного.
/// Отдельного типа на каждую категорию нет намеренно: по типу такие ошибки
/// нигде не перехватываются, а различает их значение категории, попадающее
/// в журнал и в метку метрики.
/// </summary>
public sealed class MoexHttpStatusException : MoexHttpException
{
    /// <param name="endpoint">Адрес, вернувший ошибку.</param>
    /// <param name="statusCode">Код состояния ответа.</param>
    /// <param name="category">Стабильная категория ошибки из MoexErrorTypes.</param>
    public MoexHttpStatusException(string endpoint, int statusCode, string category)
        : base($"MOEX HTTP error {statusCode} ({category}) for {endpoint}")
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ErrorCategory = category;
    }

    public override string ErrorCategory { get; }
}
