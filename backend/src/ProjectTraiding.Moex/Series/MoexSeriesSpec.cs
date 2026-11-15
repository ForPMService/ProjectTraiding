using System.Collections.Generic;
using System.Text;

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

public readonly record struct SourceColumn(int Position, byte[] Name);

public sealed class MoexSeriesSpec
{
    /// <summary>
    /// Единая форма исходных свечных колонок для истории и realtime. Отдельный массив
    /// на каждый контур инициализировал бы два набора одинаковых UTF-8 имён.
    /// </summary>
    public static readonly SourceColumn[] CandleSourceColumns =
    [
        new(0, "open"u8.ToArray()),
        new(1, "close"u8.ToArray()),
        new(2, "high"u8.ToArray()),
        new(3, "low"u8.ToArray()),
        new(4, "value"u8.ToArray()),
        new(5, "volume"u8.ToArray()),
        new(6, "begin"u8.ToArray()),
        new(7, "end"u8.ToArray()),
    ];

    public required string DataKind { get; init; }
    public required string Market { get; init; }
    public required string MethodTemplate { get; init; }
    public required string RootKey { get; init; }
    public required SourceColumn[] SourceColumns { get; init; }
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
    /// Cached HTTP columns parameter, built from the source-column names.
    /// </summary>
    public string ColumnsParam => _columnsParam ??= BuildColumnsParam();

    private string BuildColumnsParam()
    {
        string[] names = new string[SourceColumns.Length];
        for (int i = 0; i < SourceColumns.Length; i++)
            names[i] = Encoding.UTF8.GetString(SourceColumns[i].Name);

        return string.Join(',', names);
    }
}
