namespace ProjectTraiding.Moex.Clients.Errors
{
    /// <summary>
    /// Локальный rate limiter не выдал permit — запрос НЕ отправлен в MOEX.
    ///
    /// Отличие от MoexRateLimitException:
    /// - MoexRateLimitException — MOEX ответил 429, HTTP-запрос состоялся.
    /// - MoexRateLimitRejectedException — HTTP-запроса не было, наш limiter не пропустил.
    ///
    /// Не наследует MoexHttpException, потому что HTTP-взаимодействия не было.
    /// StatusCode, RetryAfter, IsRetryable — не применимы.
    /// </summary>
    public sealed class MoexRateLimitRejectedException : Exception
    {
        /// <summary>
        /// Адрес эндпоинта, который хотели вызвать.
        /// Для логов и диагностики — чтобы понять, какой запрос не прошёл.
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// Причина отказа.
        /// "queue_full" — очередь ожидания limiter'а переполнена, ждать негде.
        /// "acquire_timeout" — ждали жетон дольше AcquireTimeout, не дождались.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Сколько запрос провёл в очереди перед отказом.
        /// null, если очередь была полна и запрос отклонён мгновенно.
        /// </summary>
        public TimeSpan? WaitTime { get; }

        /// <summary>
        /// Категория ошибки для структурированного логирования.
        /// "rate_limit_rejected" — отличается от "rate_limit" (429 от MOEX),
        /// чтобы в логах сразу видеть: проблема на нашей стороне, не на стороне MOEX.
        /// </summary>
        public string ErrorCategory => "rate_limit_rejected";

        public MoexRateLimitRejectedException(string endpoint, string reason, TimeSpan? waitTime = null)
            : base($"Rate limit permit not acquired for {endpoint}: {reason}")
        {
            Endpoint = endpoint;
            Reason = reason;
            WaitTime = waitTime;
        }
    }
}