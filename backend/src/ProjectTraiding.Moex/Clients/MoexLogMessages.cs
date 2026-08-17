using Microsoft.Extensions.Logging;
using System.Net;
namespace ProjectTraiding.Moex.Clients
{
    public static partial class MoexLogMessages
    {
        /// <summary>
        /// Логирует начало загрузки на уровне endpoint-а, до вызова MOEX-клиента.
        /// </summary>
        [LoggerMessage(EventId = 100, EventName = "MoexLoadStarted", Level = LogLevel.Information, Message = "MOEX load started: route={RouteName}, source={Source}, endpoint={Endpoint}, query={QuerySummary}.")]
        public static partial void LoadStarted(ILogger logger, string routeName, string source, string endpoint, string querySummary);

        /// <summary>
        /// Логирует отправку исходящего HTTP-запроса в MOEX на уровне HttpClient handler-а.
        /// </summary>
        [LoggerMessage(EventId = 101, EventName = "MoexHttpRequestSent", Level = LogLevel.Debug, Message = "MOEX HTTP request sent: source={Source}, method={Method}, endpoint={Endpoint}.")]
        public static partial void HttpRequestSent(ILogger logger, string source, string method, string endpoint);

        /// <summary>
        /// Логирует получение HTTP-ответа от MOEX на уровне HttpClient handler-а.
        /// Постраничная запись относится к отладке; счёт ответов ведут метрики.
        /// </summary>
        [LoggerMessage(EventId = 102, EventName = "MoexHttpResponseReceived", Level = LogLevel.Debug, Message = "MOEX HTTP response received: source={Source}, method={Method}, endpoint={Endpoint}, status={StatusCode}, time={ElapsedMs}, size={ContentLength}.")]
        public static partial void HttpResponseReceived(ILogger logger, string source, string method, string endpoint, HttpStatusCode statusCode, TimeSpan elapsedMs, long? contentLength);

        /// <summary>
        /// Логирует повторную попытку запроса, которую выполняет Polly retry policy.
        /// </summary>
        [LoggerMessage(EventId = 110, EventName = "MoexRetryAttempt", Level = LogLevel.Warning, Message = "MOEX request will be retried: source={Source}, endpoint={Endpoint}, attempt={AttemptNumber}/{MaxAttempts}, error={ErrorType}, wait={Delay}, status={StatusCode}.")]
        public static partial void RetryAttempt(ILogger logger, string source, string endpoint, int attemptNumber, int maxAttempts, string errorType, TimeSpan? delay, HttpStatusCode? statusCode);

        /// <summary>
        /// Логирует успешно полученную и распарсенную страницу данных.
        /// Постраничная запись относится к отладке; счёт страниц и строк ведут метрики.
        /// </summary>
        [LoggerMessage(EventId = 120, EventName = "MoexPageReceived", Level = LogLevel.Debug, Message = "MOEX data page received: endpoint={Endpoint}, page={PageNumber}, rows={Rows}, time={ElapsedMs}.")]
        public static partial void PageReceived(ILogger logger, string endpoint, int pageNumber, int rows, TimeSpan elapsedMs);

        /// <summary>
        /// Логирует остановку cursor-пагинации с итоговым количеством страниц и строк.
        /// </summary>
        [LoggerMessage(EventId = 121, EventName = "MoexPaginationStopped", Level = LogLevel.Information, Message = "MOEX paging stopped: endpoint={Endpoint}, reason={StopReason}, pages={TotalPages}, rows={TotalRows}.")]
        public static partial void PaginationStopped(ILogger logger, string endpoint, string stopReason, int totalPages, int totalRows);

        /// <summary>
        /// Логирует остановку пагинации по фиксированному размеру страницы, например для candles.
        /// </summary>
        [LoggerMessage(EventId = 122, EventName = "MoexFixedPagePaginationStopped", Level = LogLevel.Information, Message = "MOEX fixed-size paging stopped: endpoint={Endpoint}, reason={Reason}, pages={TotalPages}, rows={TotalRows}, lastPageRows={LastPageRows}, pageLimit={PageLimit}.")]
        public static partial void FixedPagePaginationStopped(ILogger logger, string endpoint, string reason, int totalPages, int totalRows, int lastPageRows, int pageLimit);

        /// <summary>
        /// Логирует успешно полученную страницу в сценарии подневной разбивки, например для FUTOI.
        /// Постраничная запись относится к отладке; счёт страниц и строк ведут метрики.
        /// </summary>
        [LoggerMessage(EventId = 123, EventName = "MoexDaySplitPageReceived", Level = LogLevel.Debug, Message = "MOEX daily data received: endpoint={Endpoint}, date={Date}, rows={Rows}, time={ElapsedMs}.")]
        public static partial void DaySplitPageReceived(ILogger logger, string endpoint, string date, int rows, TimeSpan elapsedMs);

        /// <summary>
        /// Логирует завершение сценария подневной разбивки по диапазону дат.
        /// </summary>
        [LoggerMessage(EventId = 124, EventName = "MoexDaySplitCompleted", Level = LogLevel.Information, Message = "MOEX daily load completed: endpoint={Endpoint}, from={From}, till={Till}, days={DaysProcessed}, rows={TotalRows}.")]
        public static partial void DaySplitCompleted(ILogger logger, string endpoint, string from, string till, int daysProcessed, int totalRows);

        /// <summary>
        /// Логирует успешно полученный и распарсенный одностраничный ответ.
        /// Постраничная запись относится к отладке; счёт страниц и строк ведут метрики.
        /// </summary>
        [LoggerMessage(EventId = 125, EventName = "MoexSinglePageReceived", Level = LogLevel.Debug, Message = "MOEX single page processed: endpoint={Endpoint}, rows={Rows}, time={ElapsedMs}.")]
        public static partial void SinglePageReceived(ILogger logger, string endpoint, int rows, TimeSpan elapsedMs);

        /// <summary>
        /// Логирует типизированную ошибку HTTP/source уровня после классификации ответа MOEX.
        /// </summary>
        [LoggerMessage(EventId = 130, EventName = "MoexRequestFailed", Level = LogLevel.Error, Message = "MOEX request failed: source={Source}, endpoint={Endpoint}, error={ErrorCategory}, status={StatusCode}, timeoutSource={TimeoutSource}, message={ErrorMessage}.")]
        public static partial void RequestFailed(ILogger logger, Exception exception, string source, string endpoint, string errorCategory, HttpStatusCode? statusCode, string? timeoutSource, string errorMessage);

        /// <summary>
        /// Логирует ошибку чтения или парсинга тела ответа при успешном HTTP-статусе.
        /// </summary>
        [LoggerMessage(EventId = 131, EventName = "MoexParseFailed", Level = LogLevel.Error, Message = "MOEX response could not be read: endpoint={Endpoint}, error={ErrorCategory}, message={ErrorMessage}.")]
        public static partial void ParseFailed(ILogger logger, Exception exception, string endpoint, string errorCategory, string errorMessage);

        /// <summary>
        /// Логирует несовпадение схемы columns/data с ожидаемым контрактом парсера.
        /// </summary>
        //[LoggerMessage(EventId = 132, EventName = "MoexSchemaMismatch", Level = LogLevel.Error, Message = "MOEX response format is not expected: endpoint={Endpoint}, message={ErrorMessage}.")]
        //public static partial void SchemaMismatch(ILogger logger, Exception exception, string endpoint, string errorMessage);

        /// <summary>
        /// Логирует пустую страницу данных, если это важно отличить от обычной остановки пагинации.
        /// </summary>
        //[LoggerMessage(EventId = 140, EventName = "MoexEmptyPageReceived", Level = LogLevel.Information, Message = "MOEX empty page received: endpoint={Endpoint}, page={PageNumber}, reason={Reason}.")]
        //public static partial void EmptyPageReceived(ILogger logger, string endpoint, int pageNumber, string reason);

        // ── Rate Limiter ────────────────────────────────────────

        /// <summary>
        /// Логирует получение permit от rate limiter.
        /// Debug-уровень: пишется на каждый запрос, в production обычно выключен.
        /// В отладке позволяет увидеть, что limiter работает и сколько ждал каждый запрос.
        /// </summary>
        [LoggerMessage(
            EventId = 150,
            EventName = "MoexRateLimitAcquired",
            Level = LogLevel.Debug,
            Message = "MOEX rate limit permit acquired: endpoint={Endpoint}, waitMs={WaitMs}.")]
        public static partial void RateLimitAcquired(ILogger logger, string endpoint, double waitMs);

        /// <summary>
        /// Логирует запрос, который ждал в очереди rate limiter дольше порога.
        /// Information-уровень: сигнал, что мы подходим к пределу пропускной способности.
        /// Если таких записей много — нужно разобраться: слишком агрессивная пагинация,
        /// параллельные загрузки или лимит занижен.
        /// </summary>
        [LoggerMessage(
            EventId = 151,
            EventName = "MoexRateLimitQueued",
            Level = LogLevel.Information,
            Message = "MOEX rate limit queued: endpoint={Endpoint}, waitMs={WaitMs}.")]
        public static partial void RateLimitQueued(ILogger logger, string endpoint, double waitMs);
        [LoggerMessage(
          EventId = 152,
          EventName = "MoexOptionsApplied", Level = LogLevel.Information,
          Message = "MOEX options applied: attemptTimeout={AttemptTimeout}, totalTimeout={TotalTimeout}, bodyReadTimeout={BodyReadTimeout}, pollIntervalSeconds={PollIntervalSeconds}, instrumentFetchTimeout={InstrumentFetchTimeout}, heartbeatMinIntervalSeconds={HeartbeatMinIntervalSeconds}, maxRps={MaxRps}, maxConns={MaxConns}, maxPages={MaxPages}, workers={Workers}, algKeyConfigured={AlgKeyConfigured}.")]
        public static partial void OptionsApplied(
          ILogger logger,
          TimeSpan attemptTimeout, TimeSpan totalTimeout, TimeSpan bodyReadTimeout,
          int pollIntervalSeconds, TimeSpan instrumentFetchTimeout,
          int heartbeatMinIntervalSeconds,
          int maxRps, int maxConns, int maxPages, int workers, bool algKeyConfigured);

        /// <summary>
        /// Логирует отказ ограничителя частоты: разрешение не получено, запрос в сеть не ушёл.
        /// Уровень предупреждения: это не сбой источника, но и не нормальный режим —
        /// повторяющиеся отказы означают, что темп обращений уперся в предел.
        /// </summary>
        [LoggerMessage(
            EventId = 153,
            EventName = "MoexRateLimitRejected",
            Level = LogLevel.Warning,
            Message = "MOEX rate limit rejected: source={Source}, endpoint={Endpoint}, reason={Reason}, waitMs={WaitMs}.")]
        public static partial void RateLimitRejected(
            ILogger logger, string source, string endpoint, string reason, double waitMs);

    }

    internal static class MoexLogSources
    {
        public const string Iss = "MOEX_ISS";
        public const string Algopack = "MOEX_ALGOPACK";
        public const string Calendar = "MOEX_CALENDAR";
        public const string RealtimeRest = "MOEX_REALTIME_REST";
    }
}
