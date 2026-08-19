using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using System.Diagnostics;
using System.Threading.RateLimiting;

namespace ProjectTraiding.Moex.Clients
{
    /// <summary>
    /// DelegatingHandler, который запрашивает permit у rate limiter
    /// перед каждым HTTP-запросом к MOEX.
    ///
    /// Один экземпляр RateLimiter делится между всеми клиентами
    /// (ISS, ALGOPACK, Calendar, Realtime REST),
    /// потому что лимит MOEX — на IP, а не на endpoint.
    ///
    /// Стоит после слоя устойчивости и перед журналированием: слой устойчивости передаёт
    /// сюда каждую фактическую попытку отдельно, поэтому каждый повтор получает собственный
    /// жетон общего ограничения частоты.
    ///
    /// Если permit не получен — бросается MoexRateLimitRejectedException,
    /// запрос не уходит в сеть.
    /// </summary>
    public class MoexRateLimitHandler : DelegatingHandler
    {
        private readonly RateLimiter _limiter;
        private readonly MoexOptions _options;
        private readonly ILogger<MoexRateLimitHandler> _logger;
        private readonly string _source;

        /// <summary>
        /// Порог ожидания в миллисекундах, после которого пишется лог RateLimitQueued.
        /// Если запрос ждал жетон меньше этого — только Debug-лог.
        /// Если дольше — Information-лог как сигнал, что мы подходим к пределу.
        /// </summary>
        private const double QueuedThresholdMs = 50;

        public MoexRateLimitHandler(
            RateLimiter limiter,
            MoexOptions options,
            ILogger<MoexRateLimitHandler> logger,
            string source)
        {
            _limiter = limiter;
            _options = options;
            _logger = logger;
            _source = source;
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
            // Быстрый путь: жетон доступен прямо сейчас. Синхронная попытка не создаёт ни
            // связанного источника отмены, ни таймера и очередь не обгоняет — ограничитель
            // выдаёт жетон синхронно только при пустой очереди. Неуспешная попытка
            // освобождается и отказом не считается: ниже начинается прежнее ожидание.
            //
            // Уже отменённый вызов быстрый путь пропускает. Асинхронное получение проверяет
            // отмену до обращения к ограничителю и жетон не расходует; синхронная попытка
            // токена отмены не знает и израсходовала бы его, а обработчик записал бы
            // телеметрию успешного получения на запрос, который всё равно не уйдёт в сеть.
            RateLimitLease? fastLease = cancellationToken.IsCancellationRequested
                ? null
                : _limiter.AttemptAcquire(permitCount: 1);

            RateLimitLease lease;
            if (fastLease is { IsAcquired: true })
            {
                lease = fastLease;
            }
            else
            {
                fastLease?.Dispose();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.RateLimitAcquireTimeout);

                try
                {
                    lease = await _limiter.AcquireAsync(
                        permitCount: 1,
                        cancellationToken: timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Таймаут acquire (наш CTS сработал), а не отмена вызывающего кода.
                    TimeSpan waitTime = Stopwatch.GetElapsedTime(waitStart);
                    MoexMetrics.RateLimitRejected.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                        new KeyValuePair<string, object?>("reason", "acquire_timeout"));
                    MoexLogMessages.RateLimitRejected(
                        _logger, _source, endpoint, "acquire_timeout", waitTime.TotalMilliseconds);
                    throw new MoexRateLimitRejectedException(
                        endpoint,
                        reason: "acquire_timeout",
                        waitTime: waitTime);
                }
            }
            // Если cancellationToken отменён — OperationCanceledException пролетает наверх как есть.
            // Это правильно: вызывающий код сам отменил операцию.

            double waitMs = Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds;

            // ── 2. Проверить результат ──────────────────────────
            if (!lease.IsAcquired)
            {
                // Очередь переполнена — мгновенный отказ.
                lease.Dispose();
                MoexMetrics.RateLimitRejected.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                    new KeyValuePair<string, object?>("reason", "queue_full"));
                MoexLogMessages.RateLimitRejected(
                    _logger, _source, endpoint, "queue_full", 0);
                throw new MoexRateLimitRejectedException(
                    endpoint,
                    reason: "queue_full",
                    waitTime: null);
            }

            // ── 3. Permit получен — логируем, записываем метрики и пропускаем ───────
            using (lease)
            {
                // Всегда пишем Debug-лог.
                MoexLogMessages.RateLimitAcquired(_logger, endpoint, waitMs);

                // Метрика: время ожидания permit.
                MoexMetrics.RateLimitWaitDuration.Record(
                    waitMs,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source));

                // Если ждали дольше порога — дополнительный Information-лог + метрика.
                if (waitMs > QueuedThresholdMs)
                {
                    MoexLogMessages.RateLimitQueued(_logger, endpoint, waitMs);
                    MoexMetrics.RateLimitQueued.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source));
                }

                // Жетон получен — ниже по цепи остаются журналирование и транспорт.
                // Слой устойчивости находится выше: повторы приходят сюда поодиночке, каждая
                // попытка со своим жетоном.
                return await base.SendAsync(request, cancellationToken);
            }
        }
    }
}
