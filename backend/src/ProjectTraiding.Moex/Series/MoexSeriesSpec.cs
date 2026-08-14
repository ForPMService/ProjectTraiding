using System.Text;

namespace ProjectTraiding.Moex.Series;

public enum ColumnKind
{
    String,
    Int32,
    Int64,
    Double,
    DateTime,
}

public enum FillRule
{
    TaskSecId,
    Direct,
    SourceDateTime,
    WallClock,
    ExternalSecId,
    Constant,
}

public enum PaginationKind
{
    Cursor,
    DaySplit,
    FixedPage,
}

public readonly record struct SourceColumn(
    int Position,
    byte[] Name,
    ColumnKind Kind);

public readonly record struct TargetColumn(
    string Name,
    string ColumnType,
    FillRule FillRule,
    int SourceIndex = -1,
    int SecondSourceIndex = -1,
    bool Required = false,
    string? RequiredMessage = null,
    object? Constant = null);

public sealed class MoexSeriesSpec
{
    public required string DataKind { get; init; }
    public required string Market { get; init; }
    public required string TelemetryDataKind { get; init; }
    public required string MethodTemplate { get; init; }
    public required string RootKey { get; init; }
    public required SourceColumn[] SourceColumns { get; init; }
    public required string Table { get; init; }
    public required TargetColumn[] TargetColumns { get; init; }
    public required string TokenPrefix { get; init; }
    public required PaginationKind Pagination { get; init; }

    // Код свечного интервала MOEX (1, 10, 60, 24). Заполнен только у свечей;
    // у остальных видов пуст. Диспетчеризация по нему подключается на коммите
    // исторической загрузки свечей.
    public int? CandleInterval { get; init; }

    public string BuildColumnsParam()
    {
        string[] names = new string[SourceColumns.Length];
        for (int i = 0; i < SourceColumns.Length; i++)
            names[i] = Encoding.UTF8.GetString(SourceColumns[i].Name);

        return string.Join(',', names);
    }
}
