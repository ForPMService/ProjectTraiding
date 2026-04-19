using System;

namespace ProjectTraiding.Contracts.Common;

public sealed record JobIdResponse(
    Guid JobId,
    string? CorrelationId = null
);
