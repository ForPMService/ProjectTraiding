using System.Net;

namespace ProjectTraiding.CustomFeatures.Clients;

public static partial class CalendarHttpLogMessages
{
    [LoggerMessage(EventId = 310, EventName = "CalendarHttpResponseReceived", Level = LogLevel.Debug, Message = "MOEX HTTP response received: source={Source}, method={Method}, endpoint={Endpoint}, status={StatusCode}, time={ElapsedMs}, size={ContentLength}.")]
    public static partial void HttpResponseReceived(ILogger logger, string source, string method, string endpoint, HttpStatusCode statusCode, TimeSpan elapsedMs, long? contentLength);

    [LoggerMessage(EventId = 311, EventName = "CalendarRetryAttempt", Level = LogLevel.Warning, Message = "MOEX request will be retried: source={Source}, endpoint={Endpoint}, attempt={AttemptNumber}/{MaxAttempts}, error={ErrorType}, wait={Delay}, status={StatusCode}.")]
    public static partial void RetryAttempt(ILogger logger, string source, string endpoint, int attemptNumber, int maxAttempts, string errorType, TimeSpan? delay, HttpStatusCode? statusCode);

    [LoggerMessage(EventId = 312, EventName = "CalendarRequestFailed", Level = LogLevel.Error, Message = "MOEX request failed: source={Source}, endpoint={Endpoint}, error={ErrorCategory}, status={StatusCode}, timeoutSource={TimeoutSource}, message={ErrorMessage}.")]
    public static partial void RequestFailed(ILogger logger, Exception exception, string source, string endpoint, string errorCategory, HttpStatusCode? statusCode, string? timeoutSource, string errorMessage);
}
