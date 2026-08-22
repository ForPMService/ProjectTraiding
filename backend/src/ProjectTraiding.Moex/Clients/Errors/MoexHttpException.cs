using ProjectTraiding.Moex.Errors;

namespace ProjectTraiding.Moex.Clients.Errors;

/// <summary>
/// Базовый абстрактный класс для всех HTTP-ошибок взаимодействия с MOEX ISS API.
/// Не содержит заголовков запроса/ответа, Bearer-токенов или иных чувствительных данных.
/// </summary>
public abstract class MoexHttpException : MoexException
{
    /// <summary>HTTP статус-код ответа (null для timeout или сетевых ошибок).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Адрес эндпоинта, вызов которого привёл к ошибке.</summary>
    public string? Endpoint { get; init; }

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
