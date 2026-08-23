using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.Clients;

/// <summary>
/// Вспомогательные методы обработки HTTP-ответов для MOEX-клиентов.
/// </summary>
public static class HttpClientHelpers
{
    /// <summary>
    /// Категория ошибки по коду ответа. Единственное место, где код состояния переводится
    /// в стабильную категорию: и типизированное исключение, и журнал повторов берут её отсюда.
    /// </summary>
    public static string ClassifyStatus(int statusCode) => statusCode switch
    {
        429 => MoexErrorTypes.RateLimit,
        408 => MoexErrorTypes.Timeout,
        401 or 403 => MoexErrorTypes.Auth,
        400 => MoexErrorTypes.BadRequest,
        404 => MoexErrorTypes.NotFound,
        >= 500 => MoexErrorTypes.ServerError,
        >= 400 => MoexErrorTypes.ClientError,
        _ => MoexErrorTypes.UnexpectedStatus
    };

    /// <summary>
    /// Проверяет статус ответа и при ошибке бросает типизированное исключение.
    /// Dispose вызывается до throw, чтобы не допустить утечки соединения.
    /// При успехе ответ не dispose-ится — ответственность вызывающего кода.
    /// </summary>
    /// <param name="response">HTTP-ответ сервера.</param>
    /// <param name="endpoint">Адрес эндпоинта (только путь/метод, без Bearer-токена).</param>
    public static void EnsureSuccessOrThrow(
        HttpResponseMessage response,
        string endpoint)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int status = (int)response.StatusCode;

        response.Dispose();

        // Тайм-аут остаётся отдельным типом: у него своё свойство длительности и своя
        // ветвь в обработке. Все прочие коды различаются только категорией.
        if (status == 408)
            throw new MoexTimeoutException(endpoint);

        throw new MoexHttpStatusException(endpoint, status, ClassifyStatus(status));
    }

    /// <summary>
    /// Пытается извлечь рекомендуемую задержку из заголовка Retry-After.
    /// Поддерживает форматы delta-seconds и HTTP-date.
    /// </summary>
    /// <param name="response">HTTP-ответ, из заголовков которого читается Retry-After.</param>
    /// <returns>Задержка перед повторным запросом, или null если заголовок отсутствует.</returns>
    public static TimeSpan? TryParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfterHeader = response.Headers.RetryAfter;
        if (retryAfterHeader is null)
        {
            return null;
        }

        if (retryAfterHeader.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (retryAfterHeader.Date is DateTimeOffset date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    /// <summary>
    /// Извлекает задержку из Retry-After для DelayGenerator Polly.
    /// При HTTP-date в прошлом возвращает 1 секунду (не ждать 0).
    /// Clamp: не больше maxDelay.
    /// </summary>
    public static TimeSpan? GetRetryAfterForPolly(HttpResponseMessage response, TimeSpan maxDelay)
    {
        TimeSpan? raw = TryParseRetryAfter(response);
        if (raw is null)
            return null;

        if (raw.Value <= TimeSpan.Zero)
            raw = TimeSpan.FromSeconds(1);

        return raw.Value > maxDelay ? maxDelay : raw.Value;
    }
}
