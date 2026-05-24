using History_DataMoex.Clients.Errors;
using History_DataMoex.Options;
using System.Diagnostics;
using System.Threading.RateLimiting;

namespace History_DataMoex.Clients
{
    /// <summary>
    /// DelegatingHandler, который запрашивает permit у rate limiter
    /// перед каждым HTTP-запросом к MOEX.
    ///
    /// Один экземпляр RateLimiter делится между всеми клиентами (ISS, ALGOPACK, Calendar),
    /// потому что лимит MOEX — на IP, а не на endpoint.
    ///
    /// Стоит в pipeline перед Polly: retry-попытки тоже проходят через limiter
    /// и расходуют permit, потому что с точки зрения MOEX каждый retry —
    /// полноценный HTTP-запрос.
    ///
    /// Если permit не получен — бросается MoexRateLimitRejectedException,
    /// запрос не уходит в сеть.
    /// </summary>
    public class MoexRateLimitHandler : DelegatingHandler
    {
        private readonly RateLimiter _limiter;
        private readonly MoexOptions _options;
        private readonly ILogger<MoexRateLimitHandler> _logger;

        /// <summary>
        /// Порог ожидания в миллисекундах, после которого пишется лог RateLimitQueued.
        /// Если запрос ждал жетон меньше этого — только Debug-лог.
        /// Если дольше — Information-лог как сигнал, что мы подходим к пределу.
        /// </summary>
        private const double QueuedThresholdMs = 50;

        public MoexRateLimitHandler(
            RateLimiter limiter,
            MoexOptions options,
            ILogger<MoexRateLimitHandler> logger)
        {
            _limiter = limiter;
            _options = options;
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Endpoint для логов и ошибок.
            // PathAndQuery — без scheme/host, без Bearer-токена.
            // Тот же паттерн, что в OnRetryHandler и MoexHttpLoggingHandler.
            string endpoint = request.RequestUri?.PathAndQuery ?? "unknown";

            // ── 1. Запросить permit ─────────────────────────────
            long waitStart = Stopwatch.GetTimestamp();

            // AcquireAsync ждёт, пока появится жетон, или отменяется по:
            // - cancellationToken (вызывающий код отменил операцию)
            // - таймаут реализуем через CancellationTokenSource ниже
            //
            // Если очередь полна — AcquireAsync возвращает lease с IsAcquired = false
            // мгновенно, без ожидания.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.RateLimitAcquireTimeout);

            RateLimitLease lease;
            try
            {
                lease = await _limiter.AcquireAsync(
                    permitCount: 1,
                    cancellationToken: timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Таймаут acquire (наш CTS сработал), а не отмена вызывающего кода.
                double timeoutWaitMs = Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds;
                throw new MoexRateLimitRejectedException(
                    endpoint,
                    reason: "acquire_timeout",
                    waitTime: Stopwatch.GetElapsedTime(waitStart));
            }
            // Если cancellationToken отменён — OperationCanceledException пролетает наверх как есть.
            // Это правильно: вызывающий код сам отменил операцию.

            double waitMs = Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds;

            // ── 2. Проверить результат ──────────────────────────
            if (!lease.IsAcquired)
            {
                // Очередь переполнена — мгновенный отказ.
                lease.Dispose();
                throw new MoexRateLimitRejectedException(
                    endpoint,
                    reason: "queue_full",
                    waitTime: null);
            }

            // ── 3. Permit получен — логируем и пропускаем ───────
            using (lease)
            {
                // Всегда пишем Debug-лог.
                MoexLogMessages.RateLimitAcquired(_logger, endpoint, waitMs);

                // Если ждали дольше порога — дополнительный Information-лог.
                if (waitMs > QueuedThresholdMs)
                {
                    MoexLogMessages.RateLimitQueued(_logger, endpoint, waitMs);
                }

                // Передаём запрос дальше по цепочке handler'ов.
                // Дальше пойдёт MoexHttpLoggingHandler → Polly → SocketsHttpHandler → сеть.
                return await base.SendAsync(request, cancellationToken);
            }
        }
    }
}
