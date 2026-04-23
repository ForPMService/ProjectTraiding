namespace ProjectTraiding.Shared.Observability;

public sealed record CorrelationContext(
    string CorrelationId,
    string? TraceId = null)
{
    public static CorrelationContext Empty { get; } = new(string.Empty);

    public bool HasCorrelationId => !string.IsNullOrWhiteSpace(CorrelationId);

    public bool HasTraceId => !string.IsNullOrWhiteSpace(TraceId);
}
