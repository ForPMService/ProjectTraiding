namespace ProjectTraiding.Moex.Series;

/// <summary>Compact accounting summary for one historical series page.</summary>
public readonly record struct HistoricalSeriesParsedPage(
    int SourceRowsCount,
    int AcceptedRowsCount,
    List<SkippedSourceRow>? SkippedSourceRows,
    DateTime? LastSourceTime,
    SeriesReadFailure? Failure = null)
{
    public int SkippedRows => SkippedSourceRows?.Count ?? 0;

    /// <summary>Maximum source time among rows actually appended to the protobuf writer.</summary>
    public DateTime? MaxAcceptedSourceTime { get; init; }

    /// <summary>Valid source rows parsed, including rows filtered by the current-day cursor.</summary>
    public int ParsedValidRowsCount { get; init; }
}

/// <summary>Isolated read failure after which the next requested boundary is known.</summary>
public readonly record struct SeriesReadFailure(DateOnly DateFrom, DateOnly DateTill, string Reason);

/// <summary>Rejected source row, with its original page position and known source time.</summary>
public readonly record struct SkippedSourceRow(int SourceIndex, DateTime? Time, string Reason);
