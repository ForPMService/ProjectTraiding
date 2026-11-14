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
}
