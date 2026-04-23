namespace ProjectTraiding.Shared.Observability;

public sealed record OperationContext(
    string OperationName,
    CorrelationContext Correlation,
    string? SourceCode = null,
    string? EndpointGroup = null,
    string? InstrumentCode = null,
    string? DataNeedCode = null,
    string? JobId = null,
    string? ProbeId = null)
{
    public static OperationContext Empty { get; } = new(
        string.Empty,
        CorrelationContext.Empty);
}
