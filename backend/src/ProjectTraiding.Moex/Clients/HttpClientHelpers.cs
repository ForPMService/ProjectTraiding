using ProjectTraiding.Moex.Clients.Errors;

namespace ProjectTraiding.Moex.Clients;

/// <summary>
/// Вспомогательные методы обработки HTTP-ответов для MOEX-клиентов.
/// </summary>
public static class HttpClientHelpers
{
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

        TimeSpan? retryAfter = TryParseRetryAfter(response);
        int status = (int)response.StatusCode;

        response.Dispose();

        throw status switch
        {
            429 => new MoexRateLimitException(endpoint, retryAfter),
            408 => new MoexTimeoutException(endpoint, retryAfter),
            401 or 403 => new MoexAuthException(endpoint, status),
            400 => new MoexBadRequestException(endpoint),
            404 => new MoexNotFoundException(endpoint),
            >= 500 => new MoexServerException(endpoint, status),
            >= 400 => new MoexClientException(endpoint, status),
            _ => new MoexUnexpectedStatusException(endpoint, status)
        };
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
