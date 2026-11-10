using System.Buffers;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

public sealed class MoexHistoryWriter
{
    private static readonly Action<IBufferWriter<byte>, CandleRow> AppendCandle =
        static (buffer, row) => CandleProtobufBatchWriter.Append(buffer, in row);

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
        int readErrorCount = 0;
        string? firstReadErrorReason = null;
        DateOnly? firstReadErrorDateFrom = null;
        DateOnly? firstReadErrorDateTill = null;
        DateOnly? firstSkippedDate = null;

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

                readErrorCount++;
                if (firstReadErrorReason is null)
                {
                    firstReadErrorReason = failure.Reason;
                    firstReadErrorDateFrom = failure.DateFrom;
                    firstReadErrorDateTill = failure.DateTill;
                }
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
                    if (skippedTime is null || skippedTime.Value == default)
                        continue;

                    DateOnly skippedDate = DateOnly.FromDateTime(skippedTime.Value);
                    firstSkippedDate ??= skippedDate;
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
        return new RowWriteSummary(
            rowsRead,
            rowsSkipped,
            readErrorCount,
            firstReadErrorReason,
            firstReadErrorDateFrom,
            firstReadErrorDateTill,
            firstSkippedDate);
    }

    public async Task<RowWriteSummary> WriteCandleRangeAsync(
        MoexSeriesSpec spec,
        string secid,
        IAsyncEnumerable<CandleParsedPage> pages,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
    {
        EnsureRangeValid(spec, secid);

        List<CandleRow> batch = new(_batchSize);
        long rowsRead = 0;
        long rowsSkipped = 0;
        long rowsInsertedReported = 0;
        int readErrorCount = 0;
        string? firstReadErrorReason = null;
        DateOnly? firstReadErrorDateFrom = null;
        DateOnly? firstReadErrorDateTill = null;
        DateOnly? firstSkippedDate = null;

        await foreach (CandleParsedPage page in pages.WithCancellation(cancellationToken))
        {
            if (page.Failure is SeriesReadFailure failure)
            {
                if (batch.Count > 0)
                {
                    long reported = await InsertCandleBatchAsync(
                        spec, batch, insertContext, cancellationToken);
                    rowsInsertedReported += reported;
                    batch.Clear();
                }

                readErrorCount++;
                if (firstReadErrorReason is null)
                {
                    firstReadErrorReason = failure.Reason;
                    firstReadErrorDateFrom = failure.DateFrom;
                    firstReadErrorDateTill = failure.DateTill;
                }
                continue;
            }

            rowsRead += page.SourceRowsCount;
            rowsSkipped += page.SkippedRows;
            if (page.SkippedSourceRows is not null)
            {
                for (int i = 0; i < page.SkippedSourceRows.Count; i++)
                {
                    DateTime? skippedTime = page.SkippedSourceRows[i].Time;
                    if (skippedTime is null || skippedTime.Value == default)
                        continue;

                    DateOnly skippedDate = DateOnly.FromDateTime(skippedTime.Value);
                    firstSkippedDate ??= skippedDate;
                }
            }

            foreach ((CandleRow row, _) in page.Rows)
            {
                batch.Add(row);
                if (batch.Count >= _batchSize)
                {
                    long reported = await InsertCandleBatchAsync(
                        spec, batch, insertContext, cancellationToken);
                    rowsInsertedReported += reported;
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            long reported = await InsertCandleBatchAsync(
                spec, batch, insertContext, cancellationToken);
            rowsInsertedReported += reported;
        }

        ClickHouseWriterLogMessages.RangeWritten(
            _logger, secid, rowsRead, rowsInsertedReported);
        return new RowWriteSummary(
            rowsRead,
            rowsSkipped,
            readErrorCount,
            firstReadErrorReason,
            firstReadErrorDateFrom,
            firstReadErrorDateTill,
            firstSkippedDate);
    }

    public async Task<RowWriteSummary> WriteTradeStatsRangeAsync<TRow>(
        MoexSeriesSpec spec,
        string secid,
        IAsyncEnumerable<TradeStatsParsedPage<TRow>> pages,
        string protobufSchema,
        string messageName,
        Action<IBufferWriter<byte>, TRow> appendRow,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
        where TRow : struct
    {
        EnsureRangeValid(spec, secid);

        List<TRow> batch = new(_batchSize);
        long rowsRead = 0;
        long rowsSkipped = 0;
        long rowsInsertedReported = 0;
        int readErrorCount = 0;
        string? firstReadErrorReason = null;
        DateOnly? firstReadErrorDateFrom = null;
        DateOnly? firstReadErrorDateTill = null;
        DateOnly? firstSkippedDate = null;

        await foreach (TradeStatsParsedPage<TRow> page
                       in pages.WithCancellation(cancellationToken))
        {
            if (page.Failure is SeriesReadFailure failure)
            {
                if (batch.Count > 0)
                {
                    long reported = await InsertTradeStatsBatchAsync(
                        spec, batch, protobufSchema, messageName, appendRow,
                        insertContext, cancellationToken);
                    rowsInsertedReported += reported;
                    batch.Clear();
                }

                readErrorCount++;
                if (firstReadErrorReason is null)
                {
                    firstReadErrorReason = failure.Reason;
                    firstReadErrorDateFrom = failure.DateFrom;
                    firstReadErrorDateTill = failure.DateTill;
                }
                continue;
            }

            rowsRead += page.SourceRowsCount;
            rowsSkipped += page.SkippedRows;
            if (page.SkippedSourceRows is not null)
            {
                for (int i = 0; i < page.SkippedSourceRows.Count; i++)
                {
                    DateTime? skippedTime = page.SkippedSourceRows[i].Time;
                    if (skippedTime is null || skippedTime.Value == default)
                        continue;

                    DateOnly skippedDate = DateOnly.FromDateTime(skippedTime.Value);
                    firstSkippedDate ??= skippedDate;
                }
            }

            foreach ((TRow row, _) in page.Rows)
            {
                batch.Add(row);
                if (batch.Count >= _batchSize)
                {
                    long reported = await InsertTradeStatsBatchAsync(
                        spec, batch, protobufSchema, messageName, appendRow,
                        insertContext, cancellationToken);
                    rowsInsertedReported += reported;
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            long reported = await InsertTradeStatsBatchAsync(
                spec, batch, protobufSchema, messageName, appendRow,
                insertContext, cancellationToken);
            rowsInsertedReported += reported;
        }

        ClickHouseWriterLogMessages.RangeWritten(
            _logger, secid, rowsRead, rowsInsertedReported);
        return new RowWriteSummary(
            rowsRead,
            rowsSkipped,
            readErrorCount,
            firstReadErrorReason,
            firstReadErrorDateFrom,
            firstReadErrorDateTill,
            firstSkippedDate);
    }

    private Task<long> InsertCandleBatchAsync(
        MoexSeriesSpec spec,
        IReadOnlyList<CandleRow> rows,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
    {
        return _executor.InsertProtobufAsync(
            spec.Table,
            spec.Columns,
            CandleProtobufSchema.Definition,
            CandleProtobufSchema.MessageName,
            rows,
            AppendCandle,
            insertContext,
            cancellationToken);
    }

    private Task<long> InsertTradeStatsBatchAsync<TRow>(
        MoexSeriesSpec spec,
        IReadOnlyList<TRow> rows,
        string protobufSchema,
        string messageName,
        Action<IBufferWriter<byte>, TRow> appendRow,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
        where TRow : struct
    {
        return _executor.InsertProtobufAsync(
            spec.Table,
            spec.Columns,
            protobufSchema,
            messageName,
            rows,
            appendRow,
            insertContext,
            cancellationToken);
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
