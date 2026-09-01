using ProjectTraiding.CustomFeatures.Clients;

namespace ProjectTraiding.CustomFeatures.Errors;

public sealed class CalendarSchemaMismatchException : CalendarSourceException
{
    public override string ErrorCategory => CalendarErrorTypes.SchemaMismatch;

    public string? Endpoint { get; }

    public CalendarSchemaMismatchException(string message, string? endpoint = null)
        : base(message)
    {
        Endpoint = endpoint;
    }
}
