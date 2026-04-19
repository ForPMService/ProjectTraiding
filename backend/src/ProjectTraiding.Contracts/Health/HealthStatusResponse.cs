using System.Collections.Generic;

namespace ProjectTraiding.Contracts.Health;

public sealed record HealthStatusResponse(
    string Status,
    IReadOnlyCollection<ServiceHealthItem> Services
);
