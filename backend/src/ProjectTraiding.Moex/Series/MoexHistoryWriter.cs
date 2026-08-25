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

        await foreach (SeriesParsedPage page
                       in pages.WithCancellation(cancellationToken))
        {
            // Покрытый объём считается по строкам источника, а не по принятым: отвергнутая
            // строка прочитана и входит в диапазон, просто не записана. Иначе rows_total
            // изменил бы смысл — сегодня в нём число прочитанных строк.
            rowsRead += page.SourceRowsCount;
            rowsSkipped += page.SkippedRows;

            foreach ((object?[] row, _) in page.Rows)
            {
                batch.Add(row);
                if (batch.Count >= _batchSize)
                {
                    long reported = await FlushAsync(
                        spec,
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
            long reported = await FlushAsync(
                spec,
                spec.Columns,
                spec.ColumnTypes,
                batch,
                insertContext,
                cancellationToken);
            rowsInsertedReported += reported;
        }

        ClickHouseWriterLogMessages.RangeWritten(
            _logger, secid, rowsRead, rowsInsertedReported);
        return new RowWriteSummary(rowsRead, rowsSkipped);
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

    private async Task<long> FlushAsync(
        MoexSeriesSpec spec,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, string> columnTypes,
        IReadOnlyList<object?[]> batch,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
    {
        return await _executor.InsertAsync(
            spec.Table,
            columns,
            columnTypes,
            batch,
            insertContext,
            cancellationToken);
    }
}
