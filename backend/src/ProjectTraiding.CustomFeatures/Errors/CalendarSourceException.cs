namespace ProjectTraiding.CustomFeatures.Errors;

public abstract class CalendarSourceException : Exception
{
    public abstract string ErrorCategory { get; }

    protected CalendarSourceException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
