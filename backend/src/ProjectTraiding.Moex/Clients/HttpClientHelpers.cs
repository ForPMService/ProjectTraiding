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
    /// <summary>
    /// Максимальный размер тела ошибочного ответа, сохраняемого для диагностики.
    /// </summary>
    private const int MaxErrorBodyBytes = 65_536;

    /// <summary>
    /// Проверяет статус ответа и при ошибке бросает типизированное исключение.
    /// Перед dispose читает первые 64 KB тела ответа для диагностики (raw-capture).
    /// При успехе ответ не dispose-ится — ответственность вызывающего кода.
    /// </summary>
    /// <param name="response">HTTP-ответ сервера.</param>
    /// <param name="endpoint">Адрес эндпоинта (только путь/метод, без Bearer-токена).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task EnsureSuccessOrThrowAsync(
        HttpResponseMessage response,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        TimeSpan? retryAfter = TryParseRetryAfter(response);
        int status = (int)response.StatusCode;

        byte[]? errorBody = null;
        try
        {
            Stream bodyStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = new byte[MaxErrorBodyBytes];
            int bytesRead = await bodyStream.ReadAsync(buffer, 0, MaxErrorBodyBytes, cancellationToken);
            if (bytesRead > 0)
            {
                errorBody = new byte[bytesRead];
                Array.Copy(buffer, errorBody, bytesRead);
            }
        }
        catch
        {
            // Чтение тела не удалось — продолжаем без него.
        }

        response.Dispose();

        throw status switch
        {
            429 => new MoexRateLimitException(endpoint, retryAfter) { ErrorBody = errorBody },
            401 or 403 => new MoexAuthException(endpoint, status) { ErrorBody = errorBody },
            400 => new MoexBadRequestException(endpoint) { ErrorBody = errorBody },
            404 => new MoexNotFoundException(endpoint) { ErrorBody = errorBody },
            >= 500 => new MoexServerException(endpoint, status) { ErrorBody = errorBody },
            >= 400 => new MoexClientException(endpoint, status) { ErrorBody = errorBody },
            _ => new MoexUnexpectedStatusException(endpoint, status) { ErrorBody = errorBody }
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
