namespace ProjectTraiding.CustomFeatures.Infrastructure;

public static class CalendarMoscowTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateTime Now =>
        DateTime.SpecifyKind(DateTime.UtcNow + Offset, DateTimeKind.Unspecified);

    public static DateOnly Today => DateOnly.FromDateTime(Now);
}
