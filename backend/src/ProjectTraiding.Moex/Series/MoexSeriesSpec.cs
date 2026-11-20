using System.Collections.Generic;
using System.Collections.Immutable;

namespace ProjectTraiding.Moex.Series;

public enum PaginationKind
{
    Cursor,
    DaySplit,
    FixedPage,
}

public sealed class MoexSeriesSpec
{
    public required string DataKind { get; init; }
    public required string Market { get; init; }
    public required string MethodTemplate { get; init; }
    public required string RootKey { get; init; }
    public required ImmutableArray<string> InputColumns { get; init; }
    public required string Table { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required PaginationKind Pagination { get; init; }

    // Код свечного интервала MOEX (1, 10, 60, 24); у остальных рядов пуст.
    public int? CandleInterval { get; init; }

    /// <summary>
    /// A series may have several rows for one source timestamp, distinguished by an
    /// additional key such as a metric or alert type. The source does not guarantee
    /// the order within that group, so page boundaries must not split the group.
    /// </summary>
    public bool PreserveCursorTimeGroup { get; init; }

    private string? _columnsParam;

    /// <summary>
    /// Cached HTTP columns parameter, built from the ordered input column names.
    /// </summary>
    public string ColumnsParam => _columnsParam ??= BuildColumnsParam();

    private string BuildColumnsParam()
    {
        return string.Join(',', InputColumns);
    }
}
