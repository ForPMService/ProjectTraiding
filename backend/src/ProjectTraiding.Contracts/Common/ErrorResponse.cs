namespace ProjectTraiding.Contracts.Common;

public sealed record ErrorResponse(
    string ErrorCode,
    string Message,
    string? CorrelationId = null
);
