namespace ProjectTraiding.Moex.Series;

/// <summary>
/// Compact summary of a parsed tradestats page. Accepted market rows are appended
/// directly to their Protobuf writers and are not retained here.
/// </summary>
public readonly record struct TradeStatsParsedPage<TRow>(
    int SourceRowsCount,
    int AcceptedRowsCount,
    List<SkippedSourceRow>? SkippedSourceRows,
    DateTime? LastSourceTime,
    DateTime? MaxAcceptedSourceTime,
    int HttpSourceRowsCount,
    SeriesReadFailure? Failure = null)
    where TRow : struct
{
    public int SkippedRows => SkippedSourceRows?.Count ?? 0;
}
