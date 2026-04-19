namespace ProjectTraiding.Contracts.Health;

public sealed record ServiceHealthItem(
    string Name,
    string Status,
    string? Message = null,
    long? DurationMs = null,
    string? ErrorCode = null
);
