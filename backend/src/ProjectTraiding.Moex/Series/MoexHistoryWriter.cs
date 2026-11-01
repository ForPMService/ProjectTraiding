using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

public sealed class MoexHistoryWriter
{
    private readonly ClickHouseInsertExecutor _executor;
    private readonly ILogger<MoexHistoryWriter> _logger;
    private readonly int _batchSize;

    public MoexHistoryWriter(
        ClickHouseInsertExecutor executor,
        ILogger<MoexHistoryWriter> logger,
        int batchSize)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize), "Размер пачки должен быть положительным.");
        }

        _executor = executor;
        _logger = logger;
        _batchSize = batchSize;
    }

    public async Task<RowWriteSummary> WriteRangeAsync(
        MoexSeriesSpec spec,
        string secid,
        IAsyncEnumerable<SeriesParsedPage> pages,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
    {
        EnsureRangeValid(spec, secid);

        List<object?[]> batch = new(_batchSize);
        long rowsRead = 0;
        long rowsSkipped = 0;
        long rowsInsertedReported = 0;
        string? errorReason = null;
        DateOnly? errorDateFrom = null;
        DateOnly? errorDateTill = null;

        await foreach (SeriesParsedPage page
                       in pages.WithCancellation(cancellationToken))
        {
            if (page.Failure is SeriesReadFailure failure)
            {
                if (batch.Count > 0)
                {
                    long reported = await _executor.InsertAsync(
                        spec.Table, spec.Columns, spec.ColumnTypes, batch, insertContext,
                        cancellationToken);
                    rowsInsertedReported += reported;
                    batch.Clear();
                }

                errorReason ??= failure.Reason;
                if (errorDateFrom is null || failure.DateFrom < errorDateFrom)
                    errorDateFrom = failure.DateFrom;
                if (errorDateTill is null || failure.DateTill > errorDateTill)
                    errorDateTill = failure.DateTill;
                continue;
            }

            // Покрытый объём считается по строкам источника, а не по принятым: отвергнутая
            // строка прочитана и входит в диапазон, просто не записана. Иначе rows_total
            // изменил бы смысл — сегодня в нём число прочитанных строк.
            rowsRead += page.SourceRowsCount;
            rowsSkipped += page.SkippedRows;
            if (page.SkippedSourceRows is not null)
            {
                for (int i = 0; i < page.SkippedSourceRows.Count; i++)
                {
                    DateTime? skippedTime = page.SkippedSourceRows[i].Time;
                    if (skippedTime is null)
                        continue;

                    DateOnly skippedDate = DateOnly.FromDateTime(skippedTime.Value);
                    if (errorDateFrom is null || skippedDate < errorDateFrom)
                        errorDateFrom = skippedDate;
                    if (errorDateTill is null || skippedDate > errorDateTill)
                        errorDateTill = skippedDate;
                }
            }

            foreach ((object?[] row, _) in page.Rows)
            {
                batch.Add(row);
                if (batch.Count >= _batchSize)
                {
                    long reported = await _executor.InsertAsync(
                        spec.Table,
                        spec.Columns,
                        spec.ColumnTypes,
                        batch,
                        insertContext,
                        cancellationToken);
                    rowsInsertedReported += reported;
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            long reported = await _executor.InsertAsync(
                spec.Table,
                spec.Columns,
                spec.ColumnTypes,
                batch,
                insertContext,
                cancellationToken);
            rowsInsertedReported += reported;
        }

        ClickHouseWriterLogMessages.RangeWritten(
            _logger, secid, rowsRead, rowsInsertedReported);
        if (rowsSkipped > 0 && errorReason is null)
            errorReason = $"Историческая загрузка пропустила {rowsSkipped} строк источника.";
        return new RowWriteSummary(
            rowsRead, rowsSkipped, errorReason is not null,
            errorReason, errorDateFrom, errorDateTill);
    }

    private static void EnsureRangeValid(MoexSeriesSpec spec, string secid)
    {
        if (!string.IsNullOrWhiteSpace(secid))
            return;

        for (int i = 0; i < spec.TargetColumns.Length; i++)
        {
            TargetColumn column = spec.TargetColumns[i];
            if (column.FillRule == FillRule.TaskSecId && column.Required)
            {
                throw new InvalidOperationException(
                    column.RequiredMessage
                    ?? "Загрузка исторического ряда отвергнута: secid пустой.");
            }
        }
    }
}
