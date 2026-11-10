using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ProjectTraiding.Moex.Loading;

public sealed class SpecLoadHandler
{
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
    private readonly MoexHistoryWriter _writer;
    private readonly SeriesRangeDeleter _rangeDeleter;
    private readonly ILogger<SpecLoadHandler> _logger;

    public SpecLoadHandler(
        MoexHistoryPageReader reader,
        MoexHistoryWriter writer,
        SeriesRangeDeleter rangeDeleter,
        ILogger<SpecLoadHandler> logger)
    {
        _reader = reader;
        _writer = writer;
        _rangeDeleter = rangeDeleter;
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
            IAsyncEnumerable<CandleParsedPage> candlePages = ReadCandleRequestWindows(
                spec, task, operationTags, cancellationToken);
            StorageInsertContext candleInsertContext = new(
                operationTags.DataKind,
                operationTags.Market,
                MoexFlows.History);

            return await _writer.WriteCandleRangeAsync(
                spec,
                task.Secid,
                candlePages,
                candleInsertContext,
                cancellationToken);
        }

        IAsyncEnumerable<SeriesParsedPage> pages = ReadRequestWindows(
            spec, task, operationTags, cancellationToken);

        StorageInsertContext insertContext = new(
            operationTags.DataKind,
            operationTags.Market,
            MoexFlows.History);

        return await _writer.WriteRangeAsync(
            spec,
            task.Secid,
            pages,
            insertContext,
            cancellationToken);
    }

    private async IAsyncEnumerable<SeriesParsedPage> ReadRequestWindows(
        MoexSeriesSpec spec,
        MoexLoadTask task,
        MoexOperationTags operationTags,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int days = task.DataKind == "orderstats" && task.Market == "stock" ? 7 : 364;
        DateOnly from = task.DateFrom;
        while (true)
        {
            int remainingDays = task.DateTill.DayNumber - from.DayNumber;
            DateOnly till = remainingDays < days ? task.DateTill : from.AddDays(days - 1);
            await using IAsyncEnumerator<SeriesParsedPage> enumerator = _reader.ReadPages(
                spec, task.Secid, task.Boardid, from, till, operationTags, cancellationToken)
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
                    yield return new SeriesParsedPage(
                        [], 0, null, new SeriesReadFailure(from, till, readFailure.Message));
                    break;
                }
                if (!hasPage)
                    break;
                SeriesParsedPage page = enumerator.Current;
                if (page.SourceRowsCount > 0)
                    LastParsedPageDate = GetLastSourceDate(page);

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
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int days = task.DataKind == "orderstats" && task.Market == "stock" ? 7 : 364;
        DateOnly from = task.DateFrom;
        while (true)
        {
            int remainingDays = task.DateTill.DayNumber - from.DayNumber;
            DateOnly till = remainingDays < days ? task.DateTill : from.AddDays(days - 1);
            await using IAsyncEnumerator<CandleParsedPage> enumerator = _reader.ReadCandlePages(
                spec, task.Secid, task.Boardid, from, till, operationTags, cancellationToken)
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
                        [], 0, null, new SeriesReadFailure(from, till, readFailure.Message));
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

    private static string GetEndpoint(MoexSeriesSpec spec, MoexLoadTask task)
    {
        string board = spec.Market == "stock"
            ? task.Boardid.ToLowerInvariant()
            : task.Boardid.ToUpperInvariant();
        return string.Format(
            CultureInfo.InvariantCulture, spec.MethodTemplate, task.Secid, board);
    }

    private static DateOnly? GetLastSourceDate(SeriesParsedPage page)
    {
        if (page.SourceRowsCount == 0)
            return null;

        DateTime? time;
        List<SkippedSourceRow>? skipped = page.SkippedSourceRows;
        if (skipped is { Count: > 0 } && skipped[^1].SourceIndex == page.SourceRowsCount - 1)
            time = skipped[^1].Time;
        else
            time = page.Rows.Count > 0 ? page.Rows[^1].Time : null;

        return time.HasValue && time.Value != default
            ? DateOnly.FromDateTime(time.Value)
            : null;
    }

    private static DateOnly? GetLastSourceDate(CandleParsedPage page)
    {
        if (page.SourceRowsCount == 0)
            return null;

        DateTime? time;
        List<SkippedSourceRow>? skipped = page.SkippedSourceRows;
        if (skipped is { Count: > 0 } && skipped[^1].SourceIndex == page.SourceRowsCount - 1)
            time = skipped[^1].Time;
        else
            time = page.Rows.Count > 0 ? page.Rows[^1].Time : null;

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
