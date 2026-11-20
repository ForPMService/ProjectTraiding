using System.Collections.Generic;
using System.Collections.Immutable;

namespace ProjectTraiding.Moex.Series;

/// <summary>
/// Накопитель частей момента на время разбора одной строки данных. Живёт в кадре стека
/// разборщика и передаётся по ссылке, поэтому объектов в куче не создаёт.
///
/// Три состояния каждой части различаются без отдельного перечисления: заполнено значение —
/// часть годна; заполнен исходный текст — часть не уложилась в строгий образец;
/// не заполнено ничего — часть отсутствует.
///
/// Исходный текст сохраняется только на пути отказа. На счастливом пути оба поля текста
/// остаются пустыми, и строк не создаётся.
/// </summary>
public struct SourceTimeParts
{
    public DateOnly? Date;
    public TimeOnly? Time;
    public string? RawDate;
    public string? RawTime;

    public void Reset()
    {
        Date = null;
        Time = null;
        RawDate = null;
        RawTime = null;
    }
}

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
