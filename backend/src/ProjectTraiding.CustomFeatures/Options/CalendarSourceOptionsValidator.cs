namespace ProjectTraiding.CustomFeatures.Options;

public static class CalendarSourceOptionsValidator
{
    public static void Validate(CalendarSourceOptions options)
    {
        if (options.AttemptTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("CustomFeatures:Moex:AttemptTimeout должен быть положительным.");

        if (options.TotalRequestTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("CustomFeatures:Moex:TotalRequestTimeout должен быть положительным.");

        if (options.BodyReadTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("CustomFeatures:Moex:BodyReadTimeout должен быть положительным.");

        if (options.AttemptTimeout > options.TotalRequestTimeout)
            throw new InvalidOperationException(
                "CustomFeatures:Moex:AttemptTimeout не может превышать CustomFeatures:Moex:TotalRequestTimeout.");

        if (options.MaxConnectionsPerServer <= 0)
            throw new InvalidOperationException("CustomFeatures:Moex:MaxConnectionsPerServer должен быть положительным.");

        if (string.IsNullOrWhiteSpace(options.CertificatesDirectory))
            throw new InvalidOperationException("CustomFeatures:Moex:CertificatesDirectory не может быть пустым.");
    }
}
