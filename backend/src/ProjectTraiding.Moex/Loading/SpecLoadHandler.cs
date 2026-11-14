using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace ProjectTraiding.Moex.Loading;

public sealed class SpecLoadHandler
{
    private delegate IAsyncEnumerable<TradeStatsParsedPage<TRow>> TradeStatsPagesReader<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime,
        uint ingestPriority,
        CancellationToken cancellationToken)
        where TRow : struct;

    private delegate IAsyncEnumerable<HistoricalSeriesParsedPage> HistoricalPagesReader<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        where TRow : struct;

    private static readonly MoexSeriesSpec[] MigratedSpecs =
    [
        MoexSeriesRegistry.Hi2Stock,
        MoexSeriesRegistry.Hi2Futures,
        MoexSeriesRegistry.TradeStatsStock,
        MoexSeriesRegistry.TradeStatsFutures,
        MoexSeriesRegistry.ObStatsStock,
        MoexSeriesRegistry.ObStatsFutures,
        MoexSeriesRegistry.OrderStatsStock,
        MoexSeriesRegistry.MegaAlertsStock,
        MoexSeriesRegistry.MegaAlertsFutures,
        MoexSeriesRegistry.Futoi,
        MoexSeriesRegistry.CandlesStock1m,
        MoexSeriesRegistry.CandlesStock10m,
        MoexSeriesRegistry.CandlesStock1h,
        MoexSeriesRegistry.CandlesStock1d,
        MoexSeriesRegistry.CandlesFutures1m,
        MoexSeriesRegistry.CandlesFutures10m,
        MoexSeriesRegistry.CandlesFutures1h,
        MoexSeriesRegistry.CandlesFutures1d,
    ];

    public DateOnly? LastParsedPageDate { get; private set; }

    private readonly MoexHistoryPageReader _reader;
    private readonly SeriesRangeDeleter _rangeDeleter;
    private readonly ClickHouseInsertExecutor _executor;
    private readonly int _batchSize;
    private readonly ILogger<SpecLoadHandler> _logger;

    public SpecLoadHandler(
        MoexHistoryPageReader reader,
        SeriesRangeDeleter rangeDeleter,
        ClickHouseInsertExecutor executor,
        ILogger<SpecLoadHandler> logger,
        int batchSize)
    {
        _reader = reader;
        _rangeDeleter = rangeDeleter;
        _executor = executor;
        _batchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));
        _logger = logger;
    }

    public async Task<RowWriteSummary> LoadAsync(
        MoexLoadTask task,
        CancellationToken cancellationToken)
    {
        LastParsedPageDate = null;
        MoexSeriesSpec spec = FindSpec(task)
            ?? throw new InvalidOperationException(
                $"Нет обработчика для задачи {task.Id} (data_kind={task.DataKind}, market={task.Market}, interval={task.CandleInterval}).");

        if (spec.DataKind == "candles")
        {
            DateOnly today = MoexTime.Today;
            if (task.DateFrom <= today && task.DateTill >= today)
            {
                throw new InvalidOperationException(
                    $"Историческая загрузка свечей для диапазона " +
                    $"{task.DateFrom:yyyy-MM-dd}—{task.DateTill:yyyy-MM-dd} запрещена: " +
                    $"он включает текущую московскую дату {today:yyyy-MM-dd}, " +
                    "которую обслуживает приёмник реального времени.");
            }
        }

        if (spec.DataKind == "tradestats")
            ValidateTaskSecId(spec, task.Secid);

        // Повторная загрузка сначала удаляет полный диапазон, затем записывает его заново.
        await _rangeDeleter.DeleteAsync(
            spec, task.Secid, task.DateFrom, task.DateTill, cancellationToken);

        string operation = spec.Pagination switch
        {
            PaginationKind.DaySplit => MoexOperations.HistoryFutoiFetch,
            PaginationKind.FixedPage => MoexOperations.HistoryCandlesFetch,
            _ => MoexOperations.HistoryCursorFetch,
        };
        MoexOperationTags operationTags = new(
            MoexLogSources.Algopack,
            operation,
            MoexDataKinds.FromTaskDataKind(task.DataKind),
            spec.Market,
            MoexFlows.History);

        if (spec.DataKind == "candles" && spec.Pagination == PaginationKind.FixedPage)
        {
            if (string.IsNullOrWhiteSpace(task.Secid))
            {
                for (int i = 0; i < spec.TargetColumns.Length; i++)
                {
                    TargetColumn column = spec.TargetColumns[i];
                    if (column.FillRule == FillRule.TaskSecId && column.Required)
                        throw new InvalidOperationException(column.RequiredMessage
                            ?? "Загрузка исторического ряда отвергнута: secid пустой.");
                }
            }

            StorageInsertContext candleInsertContext = new(
                operationTags.DataKind, operationTags.Market, MoexFlows.History);
            ProtobufBatchWriter<CandleRow> candleWriter = new(
                _executor, spec.Table, spec.Columns, candleInsertContext);
            long rowsRead = 0;
            long rowsSkipped = 0;
            long rowsInsertedReported = 0;
            int readErrorCount = 0;
            string? firstReadErrorReason = null;
            DateOnly? firstReadErrorDateFrom = null;
            DateOnly? firstReadErrorDateTill = null;
            DateOnly? firstSkippedDate = null;

            await foreach (CandleParsedPage page in ReadCandleRequestWindows(
                spec, task, operationTags, candleWriter, cancellationToken).WithCancellation(cancellationToken))
            {
                if (page.Failure is SeriesReadFailure failure)
                {
                    if (candleWriter.RowCount > 0)
                        rowsInsertedReported += await candleWriter.FlushAsync(cancellationToken);
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
                        if (skippedTime is not null && skippedTime.Value != default)
                            firstSkippedDate ??= DateOnly.FromDateTime(skippedTime.Value);
                    }
                }

                if (candleWriter.RowCount >= _batchSize)
                    rowsInsertedReported += await candleWriter.FlushAsync(cancellationToken);
            }

            if (candleWriter.RowCount > 0)
                rowsInsertedReported += await candleWriter.FlushAsync(cancellationToken);

            ClickHouseWriterLogMessages.RangeWritten(_logger, task.Secid, rowsRead, rowsInsertedReported);
            return new RowWriteSummary(rowsRead, rowsSkipped, readErrorCount, firstReadErrorReason,
                firstReadErrorDateFrom, firstReadErrorDateTill, firstSkippedDate);
        }

        StorageInsertContext insertContext = new(
            operationTags.DataKind,
            operationTags.Market,
            MoexFlows.History);

        if (spec.DataKind == "tradestats" && spec.Market == "stock")
        {
            return await LoadTradeStatsRangeAsync<TradeStatsStockRow>(
                spec, task, operationTags, insertContext,
                _reader.ReadTradeStatsStockPages, cancellationToken);
        }

        if (spec.DataKind == "tradestats" && spec.Market == "futures")
        {
            return await LoadTradeStatsRangeAsync<TradeStatsFuturesRow>(
                spec, task, operationTags, insertContext,
                _reader.ReadTradeStatsFuturesPages, cancellationToken);
        }

        if (spec.DataKind == "obstats" && spec.Market == "stock")
            return await LoadHistoricalSeriesAsync<ObStatsStockRow>(spec, task, operationTags, insertContext,
                _reader.ReadObStatsStockPages, cancellationToken);
        if (spec.DataKind == "obstats" && spec.Market == "futures")
            return await LoadHistoricalSeriesAsync<ObStatsFuturesRow>(spec, task, operationTags, insertContext,
                _reader.ReadObStatsFuturesPages, cancellationToken);
        if (spec.DataKind == "orderstats" && spec.Market == "stock")
            return await LoadHistoricalSeriesAsync<OrderStatsStockRow>(spec, task, operationTags, insertContext,
                _reader.ReadOrderStatsStockPages, cancellationToken);
        if (spec.DataKind == "futoi" && spec.Market == "futures")
            return await LoadHistoricalSeriesAsync<FutoiRow>(spec, task, operationTags, insertContext,
                _reader.ReadFutoiPages, cancellationToken);
        if (spec.DataKind == "hi2" && spec.Market == "stock")
            return await LoadHistoricalSeriesAsync<Hi2StockRow>(spec, task, operationTags, insertContext,
                _reader.ReadHi2StockPages, cancellationToken);
        if (spec.DataKind == "hi2" && spec.Market == "futures")
            return await LoadHistoricalSeriesAsync<Hi2FuturesRow>(spec, task, operationTags, insertContext,
                _reader.ReadHi2FuturesPages, cancellationToken);
        if (spec.DataKind == "mega_alerts" && spec.Market == "stock")
            return await LoadHistoricalSeriesAsync<MegaAlertsStockRow>(spec, task, operationTags, insertContext,
                _reader.ReadMegaAlertsStockPages, cancellationToken);
        if (spec.DataKind == "mega_alerts" && spec.Market == "futures")
            return await LoadHistoricalSeriesAsync<MegaAlertsFuturesRow>(spec, task, operationTags, insertContext,
                _reader.ReadMegaAlertsFuturesPages, cancellationToken);

        throw new InvalidOperationException(
            $"Нет типизированного обработчика исторического ряда {spec.DataKind}/{spec.Market}.");
    }

    private async Task<RowWriteSummary> LoadTradeStatsRangeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        MoexLoadTask task,
        MoexOperationTags operationTags,
        StorageInsertContext insertContext,
        TradeStatsPagesReader<TRow> readPages,
        CancellationToken cancellationToken)
        where TRow : struct
    {
        ProtobufBatchWriter<TRow> statsWriter = new(
            _executor, spec.Table, spec.Columns, insertContext);
        StorageInsertContext candleInsertContext = new(
            MoexDataKinds.Candles, operationTags.Market, MoexFlows.History, 5);
        ProtobufBatchWriter<CandleRow> candleWriter = new(
            _executor,
            "moex_candles_5m",
            MoexSeriesRegistry.CandlesStock1m.Columns,
            candleInsertContext);

        long rowsRead = 0;
        long rowsSkipped = 0;
        long rowsInsertedReported = 0;
        int readErrorCount = 0;
        string? firstReadErrorReason = null;
        DateOnly? firstReadErrorDateFrom = null;
        DateOnly? firstReadErrorDateTill = null;
        DateOnly? firstSkippedDate = null;

        await foreach (TradeStatsParsedPage<TRow> page in ReadTradeStatsRequestWindows(
            spec, task, operationTags, readPages, statsWriter, candleWriter, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            if (page.Failure is SeriesReadFailure failure)
            {
                rowsInsertedReported += await FlushTradeStatsWritersAsync(
                    statsWriter, candleWriter, flushStats: true, flushCandles: true, cancellationToken);
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
                    if (skippedTime is not null && skippedTime.Value != default)
                        firstSkippedDate ??= DateOnly.FromDateTime(skippedTime.Value);
                }
            }

            bool flushStats = statsWriter.RowCount >= _batchSize;
            bool flushCandles = candleWriter.RowCount >= _batchSize;
            if (flushStats || flushCandles)
            {
                rowsInsertedReported += await FlushTradeStatsWritersAsync(
                    statsWriter, candleWriter, flushStats, flushCandles, cancellationToken);
            }
        }

        rowsInsertedReported += await FlushTradeStatsWritersAsync(
            statsWriter, candleWriter, flushStats: true, flushCandles: true, cancellationToken);

        ClickHouseWriterLogMessages.RangeWritten(_logger, task.Secid, rowsRead, rowsInsertedReported);
        return new RowWriteSummary(
            rowsRead, rowsSkipped, readErrorCount, firstReadErrorReason,
            firstReadErrorDateFrom, firstReadErrorDateTill, firstSkippedDate);
    }

    private static async Task<long> FlushTradeStatsWritersAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ProtobufBatchWriter<TRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        bool flushStats,
        bool flushCandles,
        CancellationToken cancellationToken)
        where TRow : struct
    {
        long statsInserted = 0;
        List<Exception>? failures = null;

        if (flushStats && statsWriter.RowCount > 0)
        {
            try
            {
                statsInserted = await statsWriter.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (flushCandles && candleWriter.RowCount > 0)
        {
            try
            {
                _ = await candleWriter.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (cancellationToken.IsCancellationRequested
            && failures is { Count: > 0 }
            && failures[0] is OperationCanceledException)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 })
            throw new AggregateException("Обе вставки TradeStats завершились с ошибкой.", failures);

        return statsInserted;
    }

    private async Task<RowWriteSummary> LoadHistoricalSeriesAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        MoexLoadTask task,
        MoexOperationTags operationTags,
        StorageInsertContext insertContext,
        HistoricalPagesReader<TRow> readPages,
        CancellationToken cancellationToken)
        where TRow : struct
    {
        if (string.IsNullOrWhiteSpace(task.Secid))
        {
            for (int index = 0; index < spec.TargetColumns.Length; index++)
            {
                TargetColumn column = spec.TargetColumns[index];
                if (column.FillRule == FillRule.TaskSecId && column.Required)
                {
                    throw new InvalidOperationException(
                        column.RequiredMessage
                        ?? "Загрузка исторического ряда отвергнута: secid пустой.");
                }
            }
        }

        ProtobufBatchWriter<TRow> writer = new(
            _executor, spec.Table, spec.Columns, insertContext);
        long rowsRead = 0;
        long rowsSkipped = 0;
        long rowsInsertedReported = 0;
        int readErrorCount = 0;
        string? firstReadErrorReason = null;
        DateOnly? firstReadErrorDateFrom = null;
        DateOnly? firstReadErrorDateTill = null;
        DateOnly? firstSkippedDate = null;

        await foreach (HistoricalSeriesParsedPage page in ReadHistoricalRequestWindows(
            spec, task, operationTags, writer, readPages, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            if (page.Failure is SeriesReadFailure failure)
            {
                if (writer.RowCount > 0)
                    rowsInsertedReported += await writer.FlushAsync(cancellationToken);
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
                for (int index = 0; index < page.SkippedSourceRows.Count; index++)
                {
                    DateTime? skippedTime = page.SkippedSourceRows[index].Time;
                    if (skippedTime is not null && skippedTime.Value != default)
                        firstSkippedDate ??= DateOnly.FromDateTime(skippedTime.Value);
                }
            }

            if (writer.RowCount >= _batchSize)
                rowsInsertedReported += await writer.FlushAsync(cancellationToken);
        }

        if (writer.RowCount > 0)
            rowsInsertedReported += await writer.FlushAsync(cancellationToken);

        ClickHouseWriterLogMessages.RangeWritten(_logger, task.Secid, rowsRead, rowsInsertedReported);
        return new RowWriteSummary(
            rowsRead, rowsSkipped, readErrorCount, firstReadErrorReason,
            firstReadErrorDateFrom, firstReadErrorDateTill, firstSkippedDate);
    }

    private async IAsyncEnumerable<HistoricalSeriesParsedPage> ReadHistoricalRequestWindows<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        MoexLoadTask task,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> writer,
        HistoricalPagesReader<TRow> readPages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRow : struct
    {
        int days = spec.DataKind == "orderstats" && spec.Market == "stock" ? 7 : 364;
        DateOnly from = task.DateFrom;
        while (true)
        {
            int remainingDays = task.DateTill.DayNumber - from.DayNumber;
            DateOnly till = remainingDays < days ? task.DateTill : from.AddDays(days - 1);
            await using IAsyncEnumerator<HistoricalSeriesParsedPage> enumerator = readPages(
                spec, task.Secid, from, till, operationTags, writer, null, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasPage;
                Exception? readFailure = null;
                try
                {
                    hasPage = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (MoexHistoryPageReader.IsRecoverableReadFailure(ex))
                {
                    readFailure = ex;
                    hasPage = false;
                }
                catch (Exception ex)
                {
                    string endpoint = GetEndpoint(spec, task);
                    MoexLogMessages.HistoryOperationFailed(
                        _logger, ex, task.Secid, operationTags.DataKind, operationTags.Operation,
                        endpoint,
                        from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        "window", MoexMetrics.ClassifyError(ex), ex.Message);
                    throw;
                }

                if (readFailure is not null)
                {
                    yield return new HistoricalSeriesParsedPage(
                        0, 0, null, null,
                        new SeriesReadFailure(from, till, readFailure.Message));
                    break;
                }
                if (!hasPage)
                    break;

                HistoricalSeriesParsedPage page = enumerator.Current;
                if (page.SourceRowsCount > 0)
                {
                    DateTime? time = page.LastSourceTime;
                    LastParsedPageDate = time.HasValue && time.Value != default
                        ? DateOnly.FromDateTime(time.Value)
                        : null;
                }
                yield return page;
            }

            if (till == task.DateTill)
                break;
            from = till.AddDays(1);
        }
    }

    private async IAsyncEnumerable<CandleParsedPage> ReadCandleRequestWindows(
        MoexSeriesSpec spec,
        MoexLoadTask task,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<CandleRow> writer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int days = task.DataKind == "orderstats" && task.Market == "stock" ? 7 : 364;
        DateOnly from = task.DateFrom;
        while (true)
        {
            int remainingDays = task.DateTill.DayNumber - from.DayNumber;
            DateOnly till = remainingDays < days ? task.DateTill : from.AddDays(days - 1);
            await using IAsyncEnumerator<CandleParsedPage> enumerator = _reader.ReadCandlePages(
                spec, task.Secid, task.Boardid, from, till, operationTags, writer, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasPage;
                Exception? readFailure = null;
                try
                {
                    hasPage = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (MoexHistoryPageReader.IsRecoverableReadFailure(ex))
                {
                    readFailure = ex;
                    hasPage = false;
                }
                catch (Exception ex)
                {
                    string endpoint = GetEndpoint(spec, task);
                    MoexLogMessages.HistoryOperationFailed(
                        _logger,
                        ex,
                        task.Secid,
                        operationTags.DataKind,
                        operationTags.Operation,
                        endpoint,
                        from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        "window",
                        MoexMetrics.ClassifyError(ex),
                        ex.Message);
                    throw;
                }
                if (readFailure is not null)
                {
                    yield return new CandleParsedPage(
                        0, 0, null, null, new SeriesReadFailure(from, till, readFailure.Message));
                    break;
                }
                if (!hasPage)
                    break;

                CandleParsedPage page = enumerator.Current;
                if (page.SourceRowsCount > 0)
                    LastParsedPageDate = GetLastSourceDate(page);

                yield return page;
            }

            if (till == task.DateTill)
                break;
            from = till.AddDays(1);
        }
    }

    private async IAsyncEnumerable<TradeStatsParsedPage<TRow>> ReadTradeStatsRequestWindows<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        MoexLoadTask task,
        MoexOperationTags operationTags,
        TradeStatsPagesReader<TRow> readPages,
        ProtobufBatchWriter<TRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRow : struct
    {
        int days = 364;
        DateOnly from = task.DateFrom;
        while (true)
        {
            int remainingDays = task.DateTill.DayNumber - from.DayNumber;
            DateOnly till = remainingDays < days ? task.DateTill : from.AddDays(days - 1);
            await using IAsyncEnumerator<TradeStatsParsedPage<TRow>> enumerator =
                readPages(
                    spec, task.Secid, from, till, operationTags,
                    statsWriter, candleWriter, null, 1, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasPage;
                Exception? readFailure = null;
                try
                {
                    hasPage = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (MoexHistoryPageReader.IsRecoverableReadFailure(ex))
                {
                    readFailure = ex;
                    hasPage = false;
                }
                catch (Exception ex)
                {
                    string endpoint = GetEndpoint(spec, task);
                    MoexLogMessages.HistoryOperationFailed(
                        _logger,
                        ex,
                        task.Secid,
                        operationTags.DataKind,
                        operationTags.Operation,
                        endpoint,
                        from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        "window",
                        MoexMetrics.ClassifyError(ex),
                        ex.Message);
                    throw;
                }

                if (readFailure is not null)
                {
                    yield return new TradeStatsParsedPage<TRow>(
                        0, 0, null, null, null, 0,
                        new SeriesReadFailure(from, till, readFailure.Message));
                    break;
                }

                if (!hasPage)
                    break;

                TradeStatsParsedPage<TRow> page = enumerator.Current;
                if (page.SourceRowsCount > 0)
                    LastParsedPageDate = GetLastSourceDate(page);

                yield return page;
            }

            if (till == task.DateTill)
                break;
            from = till.AddDays(1);
        }
    }

    private static string GetEndpoint(MoexSeriesSpec spec, MoexLoadTask task)
    {
        string board = spec.Market == "stock"
            ? task.Boardid.ToLowerInvariant()
            : task.Boardid.ToUpperInvariant();
        return string.Format(
            CultureInfo.InvariantCulture, spec.MethodTemplate, task.Secid, board);
    }

    private static void ValidateTaskSecId(MoexSeriesSpec spec, string secid)
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

    private static DateOnly? GetLastSourceDate(CandleParsedPage page)
    {
        DateTime? time = page.LastSourceTime;
        return time.HasValue && time.Value != default
            ? DateOnly.FromDateTime(time.Value)
            : null;
    }

    private static DateOnly? GetLastSourceDate<TRow>(TradeStatsParsedPage<TRow> page)
        where TRow : struct
    {
        DateTime? time = page.SourceRowsCount == 0 ? null : page.LastSourceTime;
        return time.HasValue && time.Value != default
            ? DateOnly.FromDateTime(time.Value)
            : null;
    }

    private static MoexSeriesSpec? FindSpec(MoexLoadTask task)
    {
        for (int i = 0; i < MigratedSpecs.Length; i++)
        {
            MoexSeriesSpec spec = MigratedSpecs[i];
            if (task.DataKind == spec.DataKind
                && task.Market == spec.Market
                && task.CandleInterval == spec.CandleInterval)
                return spec;
        }

        return null;
    }
}
