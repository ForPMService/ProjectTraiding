namespace ProjectTraiding.Moex.Series;

/// <summary>Page counts and source-time diagnostics; market rows are written during parsing.</summary>
public readonly record struct CandleParsedPage(
    int SourceRowsCount,
    int AcceptedRowsCount,
    List<SkippedSourceRow>? SkippedSourceRows,
    DateTime? LastSourceTime,
    SeriesReadFailure? Failure = null)
{
    public int SkippedRows => SkippedSourceRows?.Count ?? 0;
}
