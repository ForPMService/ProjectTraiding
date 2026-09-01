namespace ProjectTraiding.CustomFeatures.Errors;

public sealed class CalendarHttpStatusException : CalendarHttpException
{
    public CalendarHttpStatusException(string endpoint, int statusCode, string category)
        : base($"MOEX HTTP error {statusCode} ({category}) for {endpoint}")
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ErrorCategory = category;
    }

    public override string ErrorCategory { get; }
}
