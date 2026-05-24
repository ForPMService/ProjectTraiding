namespace History_DataMoex.Contracts.Pagination;

/// <summary>
/// Результат решения о пагинации: продолжать или остановиться.
/// </summary>
public readonly record struct PaginationStep
{
    /// <summary>true = остановить цикл пагинации.</summary>
    public bool IsStop { get; init; }

    /// <summary>Следующее значение start для query string (если !IsStop).</summary>
    public int NextStart { get; init; }

    /// <summary>Причина остановки (если IsStop): empty_cursor, range_exhausted, safety_cap_hit.</summary>
    public string? StopReason { get; init; }

    public static PaginationStep Continue(int nextStart)
        => new() { IsStop = false, NextStart = nextStart, StopReason = null };

    public static PaginationStep Stop(string reason)
        => new() { IsStop = true, NextStart = 0, StopReason = reason };
}
