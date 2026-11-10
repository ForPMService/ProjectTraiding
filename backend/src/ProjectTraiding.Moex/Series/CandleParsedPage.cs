using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

/// <summary>
/// A typed candle page. Time metadata stays in MOEX Moscow wall-clock time and is
/// kept separately from the Unix-seconds value serialized into <see cref="CandleRow.Begin"/>.
/// </summary>
public readonly record struct CandleParsedPage(
    List<(CandleRow Row, DateTime Time)> Rows,
    int SourceRowsCount,
    List<SkippedSourceRow>? SkippedSourceRows,
    SeriesReadFailure? Failure = null)
{
    public int SkippedRows => SkippedSourceRows?.Count ?? 0;
}
