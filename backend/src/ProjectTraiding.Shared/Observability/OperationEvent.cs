namespace ProjectTraiding.Shared.Observability;

public sealed record OperationEvent(
    DateTimeOffset Timestamp,
    string Level,
    string ServiceName,
    string Environment,
    string OperationName,
    string Message,
    string CorrelationId,
    string? TraceId = null,
    string? SourceCode = null,
    string? EndpointGroup = null,
    string? InstrumentCode = null,
    string? DataNeedCode = null,
    string? JobId = null,
    string? ProbeId = null,
    ErrorCode? ErrorCode = null,
    string? ExceptionType = null,
    IReadOnlyDictionary<string, string>? Details = null);
