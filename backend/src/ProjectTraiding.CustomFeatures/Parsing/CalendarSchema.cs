using System.Diagnostics.CodeAnalysis;
using ProjectTraiding.CustomFeatures.Errors;

namespace ProjectTraiding.CustomFeatures.Parsing;

internal static class CalendarSchema
{
    [DoesNotReturn]
    internal static void Mismatch(string message)
    {
        throw new CalendarSchemaMismatchException(message: message);
    }
}
