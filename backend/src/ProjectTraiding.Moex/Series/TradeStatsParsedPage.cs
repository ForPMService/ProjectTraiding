namespace ProjectTraiding.Moex.Series;

/// <summary>
/// Типизированные строки страницы tradestats. Время остаётся московской меткой
/// источника и отдельно от значения Unix-секунд, подготовленного для Protobuf.
/// </summary>
public readonly record struct TradeStatsParsedPage<TRow>(
    List<(TRow Row, DateTime Time)> Rows,
    int SourceRowsCount,
    List<SkippedSourceRow>? SkippedSourceRows,
    SeriesReadFailure? Failure = null)
    where TRow : struct
{
    public int SkippedRows => SkippedSourceRows?.Count ?? 0;
}
