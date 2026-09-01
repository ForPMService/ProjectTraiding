using ProjectTraiding.CustomFeatures.Clients;

namespace ProjectTraiding.CustomFeatures.Errors;

public sealed class CalendarTimeoutException : CalendarHttpException
{
    public string TimeoutSource { get; }

    public CalendarTimeoutException(string message, string endpoint, string timeoutSource, Exception? inner = null)
        : base(message, inner)
    {
        Endpoint = endpoint;
        TimeoutSource = timeoutSource;
    }

    public CalendarTimeoutException(string endpoint)
        : base($"MOEX request timeout (408) for {endpoint}")
    {
        StatusCode = 408;
        Endpoint = endpoint;
        TimeoutSource = "http_status";
    }

    public override string ErrorCategory => CalendarErrorTypes.Timeout;
}
