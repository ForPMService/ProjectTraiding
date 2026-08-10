using System.Globalization;
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
        string sourceContractVersion,
        string writerVersion,
        IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> pages,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
    {
        EnsureRangeValid(spec, secid);

        string[] columns = new string[spec.TargetColumns.Length];
        Dictionary<string, string> columnTypes =
            new(spec.TargetColumns.Length, StringComparer.Ordinal);
        for (int i = 0; i < spec.TargetColumns.Length; i++)
        {
            TargetColumn column = spec.TargetColumns[i];
            columns[i] = column.Name;
            columnTypes.Add(column.Name, column.ColumnType);
        }

        List<object?[]> batch = new(_batchSize);
        DateTime batchFirstTime = default;
        DateTime batchLastTime = default;
        long rowsRead = 0;
        long rowsInsertedReported = 0;
        string? lastToken = null;

        await foreach (List<(object?[] Row, DateTime Time)> page
                       in pages.WithCancellation(cancellationToken))
        {
            foreach ((object?[] row, DateTime time) in page)
            {
                if (batch.Count == 0)
                    batchFirstTime = time;

                batchLastTime = time;
                batch.Add(row);
                rowsRead++;

                if (batch.Count >= _batchSize)
                {
                    (lastToken, long reported) = await FlushAsync(
                        spec,
                        secid,
                        sourceContractVersion,
                        writerVersion,
                        columns,
                        columnTypes,
                        batch,
                        batchFirstTime,
                        batchLastTime,
                        insertContext,
                        cancellationToken);
                    rowsInsertedReported += reported;
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            (lastToken, long reported) = await FlushAsync(
                spec,
                secid,
                sourceContractVersion,
                writerVersion,
                columns,
                columnTypes,
                batch,
                batchFirstTime,
                batchLastTime,
                insertContext,
                cancellationToken);
            rowsInsertedReported += reported;
        }

        ClickHouseWriterLogMessages.RangeWritten(
            _logger, secid, rowsRead, rowsInsertedReported);
        return new RowWriteSummary(rowsRead, rowsInsertedReported, lastToken);
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

    private async Task<(string Token, long Reported)> FlushAsync(
        MoexSeriesSpec spec,
        string secid,
        string sourceContractVersion,
        string writerVersion,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, string> columnTypes,
        IReadOnlyList<object?[]> batch,
        DateTime firstTime,
        DateTime lastTime,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
    {
        string token = string.Create(
            CultureInfo.InvariantCulture,
            $"{spec.TokenPrefix}:{secid}:{firstTime:yyyy-MM-ddTHH:mm:ss.fff}:{lastTime:yyyy-MM-ddTHH:mm:ss.fff}:{batch.Count}:{sourceContractVersion}:{writerVersion}");
        long reported = await _executor.InsertAsync(
            spec.Table,
            columns,
            columnTypes,
            batch,
            token,
            insertContext,
            cancellationToken);
        return (token, reported);
    }
}
