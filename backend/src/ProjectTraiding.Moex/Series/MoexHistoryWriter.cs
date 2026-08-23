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
        string dataGeneration,
        string sourceContractVersion,
        string writerVersion,
        IAsyncEnumerable<SeriesParsedPage> pages,
        StorageInsertContext insertContext,
        CancellationToken cancellationToken)
    {
        EnsureRangeValid(spec, secid);

        List<object?[]> batch = new(_batchSize);
        DateTime batchFirstTime = default;
        DateTime batchLastTime = default;
        long rowsRead = 0;
        long rowsInsertedReported = 0;
        string? lastToken = null;

        await foreach (SeriesParsedPage page
                       in pages.WithCancellation(cancellationToken))
        {
            // Покрытый объём считается по строкам источника, а не по принятым: отвергнутая
            // строка прочитана и входит в диапазон, просто не записана. Иначе rows_total
            // изменил бы смысл — сегодня в нём число прочитанных строк.
            rowsRead += page.SourceRowsCount;

            foreach ((object?[] row, DateTime time) in page.Rows)
            {
                if (batch.Count == 0)
                    batchFirstTime = time;

                batchLastTime = time;
                batch.Add(row);
                if (batch.Count >= _batchSize)
                {
                    (lastToken, long reported) = await FlushAsync(
                        spec,
                        secid,
                        dataGeneration,
                        sourceContractVersion,
                        writerVersion,
                        spec.Columns,
                        spec.ColumnTypes,
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
                dataGeneration,
                sourceContractVersion,
                writerVersion,
                spec.Columns,
                spec.ColumnTypes,
                batch,
                batchFirstTime,
                batchLastTime,
                insertContext,
                cancellationToken);
            rowsInsertedReported += reported;
        }

        ClickHouseWriterLogMessages.RangeWritten(
            _logger, secid, rowsRead, rowsInsertedReported);
        return new RowWriteSummary(rowsRead, lastToken);
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
        string dataGeneration,
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
        // Поколение данных стоит в токене, потому что контрольные суммы блоков в ClickHouse
        // переживают удаление строк мутацией ALTER ... DELETE: без него повторная загрузка удалённого диапазона
        // отсекается как повтор. Внутри одного поколения токен остаётся детерминированным —
        // повтор той же пачки другим заданием по-прежнему отсекается, и это проверяемое свойство.
        string token = string.Create(
            CultureInfo.InvariantCulture,
            $"{spec.TokenPrefix}:{secid}:{dataGeneration}:{firstTime:yyyy-MM-ddTHH:mm:ss.fff}:{lastTime:yyyy-MM-ddTHH:mm:ss.fff}:{batch.Count}:{sourceContractVersion}:{writerVersion}");
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
