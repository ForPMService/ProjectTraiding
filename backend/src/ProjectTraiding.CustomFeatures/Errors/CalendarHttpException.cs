namespace ProjectTraiding.CustomFeatures.Errors;

public abstract class CalendarHttpException : CalendarSourceException
{
    public int? StatusCode { get; init; }

    public string? Endpoint { get; init; }

    protected CalendarHttpException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
