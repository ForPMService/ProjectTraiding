using ProjectTraiding.Moex.Realtime.Series;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

/// <summary>
/// Писатель одной готовой пачки строк приёма. В отличие от прежнего обобщённого писателя
/// не преобразует пачку повторно: строки уже собраны разборщиком по декларации, и тот же
/// список уходит исполнителю вставки без копирования.
///
/// Логгер не внедряется намеренно: у прежнего писателя он был, но не использовался ни разу,
/// а запись о вставке и об отказе делает исполнитель.
///
/// ReplacingMergeTree схлопывает совпавшие по ключу сортировки строки при слиянии. Чтение
/// без дублей до слияния требует FINAL — это обязанность читателей.
/// </summary>
public sealed class RealtimeSpecRowWriter
{
    private readonly ClickHouseInsertExecutor _executor;

    public RealtimeSpecRowWriter(ClickHouseInsertExecutor executor)
    {
        _executor = executor;
    }

    /// <summary>
    /// Пишет готовый список строк одним обращением. Пустой список — ноль вставок, не отказ.
    /// Порядок проверок повторяет прежний писатель: сначала пустая пачка, затем код инструмента.
    /// </summary>
    public async Task WriteAsync(
        MoexRealtimeSpec spec,
        string secid,
        List<object?[]> rows,
        StorageInsertContext insertContext,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(secid))
            throw new InvalidOperationException(spec.EmptySecidMessage);

        await _executor.InsertAsync(
            spec.Table, spec.Columns, spec.ColumnTypes, rows, insertContext, ct);
    }
}
