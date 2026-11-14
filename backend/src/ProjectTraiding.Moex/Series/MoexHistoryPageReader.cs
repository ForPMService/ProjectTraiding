using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

public sealed class MoexHistoryPageReader
{
    private const int FixedPageSize = 500;

    private readonly MoexHttpAlgClient _client;
    private readonly MoexOptions _options;
    private readonly ILogger<MoexHistoryPageReader> _logger;

    /// <summary>Итог получения одной страницы: строки, курсор ответа и затраченное время.</summary>
    private readonly record struct PageFetchResult<TPage>(
        TPage Rows,
        PaginationCursorDTO Cursor,
        TimeSpan Elapsed);

    private delegate TPage PageParser<TPage>(
        ReadOnlyMemory<byte> body,
        out PaginationCursorDTO cursor);

    private delegate HistoricalSeriesParsedPage HistoricalPageParser<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ReadOnlyMemory<byte> body,
        MoexSeriesSpec spec,
        string secId,
        ProtobufBatchWriter<TRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor,
        out HistoricalSeriesTimeGroupTail<TRow>? tail)
        where TRow : struct;

    public MoexHistoryPageReader(
        MoexHttpAlgClient client,
        IOptions<MoexOptions> options,
        ILogger<MoexHistoryPageReader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public IAsyncEnumerable<TradeStatsParsedPage<TradeStatsStockRow>> ReadTradeStatsStockPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TradeStatsStockRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime,
        uint ingestPriority,
        CancellationToken cancellationToken)
    {
        PageParser<TradeStatsParsedPage<TradeStatsStockRow>> parsePage =
            (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
                TradeStatsHistoryParser.ParseStock(
                    body.Span, spec, secId, statsWriter, candleWriter,
                    lastSourceTime, ingestPriority, out cursor);
        return ReadTradeStatsCursorPages<TradeStatsStockRow>(
            spec, secId, from, till, operationTags, parsePage, cancellationToken);
    }

    public IAsyncEnumerable<TradeStatsParsedPage<TradeStatsFuturesRow>> ReadTradeStatsFuturesPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TradeStatsFuturesRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime,
        uint ingestPriority,
        CancellationToken cancellationToken)
    {
        PageParser<TradeStatsParsedPage<TradeStatsFuturesRow>> parsePage =
            (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
                TradeStatsHistoryParser.ParseFutures(
                    body.Span, spec, secId, statsWriter, candleWriter,
                    lastSourceTime, ingestPriority, out cursor);
        return ReadTradeStatsCursorPages<TradeStatsFuturesRow>(
            spec, secId, from, till, operationTags, parsePage, cancellationToken);
    }

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadObStatsStockPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<ObStatsStockRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedCursorPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseObStatsStockPage, cancellationToken);

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadObStatsFuturesPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<ObStatsFuturesRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedCursorPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseObStatsFuturesPage, cancellationToken);

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadOrderStatsStockPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<OrderStatsStockRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedCursorPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseOrderStatsStockPage, cancellationToken);

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadFutoiPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<FutoiRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedDaySplitPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseFutoiPage, cancellationToken);

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadHi2StockPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<Hi2StockRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedCursorPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseHi2StockPage, cancellationToken);

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadHi2FuturesPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<Hi2FuturesRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedCursorPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseHi2FuturesPage, cancellationToken);

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadMegaAlertsStockPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<MegaAlertsStockRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedCursorPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseMegaAlertsStockPage, cancellationToken);

    public IAsyncEnumerable<HistoricalSeriesParsedPage> ReadMegaAlertsFuturesPages(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<MegaAlertsFuturesRow> writer,
        DateTime? lastSourceTime,
        CancellationToken cancellationToken)
        => ReadTypedCursorPages(spec, secId, from, till, operationTags, writer, lastSourceTime, ParseMegaAlertsFuturesPage, cancellationToken);

    /// <summary>Typed fixed-page route used by historical candle intervals.</summary>
    public IAsyncEnumerable<CandleParsedPage> ReadCandlePages(
        MoexSeriesSpec spec,
        string secId,
        string boardId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<CandleRow> writer,
        CancellationToken cancellationToken)
    {
        if (spec.DataKind != "candles" || spec.Pagination != PaginationKind.FixedPage)
        {
            throw new ArgumentException(
                "Typed candle pages require a fixed-page candle series specification.",
                nameof(spec));
        }

        return ReadFixedCandlePages(
            spec, secId, boardId, from, till, operationTags, writer, cancellationToken);
    }

    private static HistoricalSeriesParsedPage ParseObStatsStockPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<ObStatsStockRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<ObStatsStockRow>? tail)
        => ObStatsHistoryParser.ParseStock(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private static HistoricalSeriesParsedPage ParseObStatsFuturesPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<ObStatsFuturesRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<ObStatsFuturesRow>? tail)
        => ObStatsHistoryParser.ParseFutures(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private static HistoricalSeriesParsedPage ParseOrderStatsStockPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<OrderStatsStockRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<OrderStatsStockRow>? tail)
        => OrderStatsHistoryParser.Parse(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private static HistoricalSeriesParsedPage ParseFutoiPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<FutoiRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<FutoiRow>? tail)
        => FutoiHistoryParser.Parse(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private static HistoricalSeriesParsedPage ParseHi2StockPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<Hi2StockRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<Hi2StockRow>? tail)
        => Hi2HistoryParser.ParseStock(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private static HistoricalSeriesParsedPage ParseHi2FuturesPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<Hi2FuturesRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<Hi2FuturesRow>? tail)
        => Hi2HistoryParser.ParseFutures(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private static HistoricalSeriesParsedPage ParseMegaAlertsStockPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<MegaAlertsStockRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<MegaAlertsStockRow>? tail)
        => MegaAlertsHistoryParser.ParseStock(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private static HistoricalSeriesParsedPage ParseMegaAlertsFuturesPage(
        ReadOnlyMemory<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<MegaAlertsFuturesRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor, out HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow>? tail)
        => MegaAlertsHistoryParser.ParseFutures(body.Span, spec, secId, writer, lastSourceTime, out cursor, out tail);

    private async IAsyncEnumerable<CandleParsedPage> ReadFixedCandlePages(
        MoexSeriesSpec spec,
        string secId,
        string boardId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<CandleRow> writer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (spec.CandleInterval is not int candleInterval)
        {
            throw new InvalidOperationException(
                $"Декларация {spec.DataKind}/{spec.Market} с постраничной пагинацией без интервала.");
        }

        string board = spec.Market == "stock"
            ? boardId.ToLowerInvariant()
            : boardId.ToUpperInvariant();
        string method = string.Format(
            CultureInfo.InvariantCulture, spec.MethodTemplate, secId, board);
        Dictionary<string, string> query = new()
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["till"] = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["interval"] = candleInterval.ToString(CultureInfo.InvariantCulture),
            ["iss.meta"] = "off",
            ["iss.only"] = spec.RootKey,
        };

        PageParser<CandleParsedPage> parsePage =
            (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
                CandleHistoryParser.Parse(body.Span, spec, secId, writer, out cursor);

        int queryStart = 0;
        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult<CandleParsedPage> page = await FetchPageAsync(
                spec, method, query, secId, operationTags,
                parsePage, static parsed => parsed.AcceptedRowsCount, cancellationToken);
            CandleParsedPage parsed = page.Rows;

            AccountSkippedRows(parsed, method, secId, operationTags, query);
            pagesElapsed++;
            totalRows += parsed.AcceptedRowsCount;
            MoexLogMessages.PageReceived(
                _logger, method, pagesElapsed, parsed.AcceptedRowsCount, page.Elapsed);

            yield return parsed;

            if (parsed.SourceRowsCount >= FixedPageSize)
            {
                if (pagesElapsed >= _options.MaxPagesPerLoad)
                {
                    throw new InvalidOperationException(
                        $"Достигнут защитный предел Moex:MaxPagesPerLoad={_options.MaxPagesPerLoad} " +
                        "при неисчерпанном диапазоне.");
                }

                queryStart += FixedPageSize;
                query["start"] = queryStart.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                MoexLogMessages.FixedPagePaginationStopped(
                    _logger, method, "last_page_incomplete",
                    pagesElapsed, totalRows, parsed.AcceptedRowsCount, FixedPageSize);
                break;
            }
        }
    }

    private async IAsyncEnumerable<TradeStatsParsedPage<TRow>> ReadTradeStatsCursorPages<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        PageParser<TradeStatsParsedPage<TRow>> parsePage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRow : struct
    {
        if (spec.DataKind != "tradestats" || spec.Pagination != PaginationKind.Cursor)
        {
            throw new ArgumentException(
                "Typed trade statistics pages require a cursor-paginated tradestats series.",
                nameof(spec));
        }

        string method = string.Format(
            CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        Dictionary<string, string> query = new()
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["till"] = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["iss.meta"] = "off",
            ["iss.only"] = "data,data.cursor",
            ["data.columns"] = spec.ColumnsParam,
        };

        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult<TradeStatsParsedPage<TRow>> page = await FetchPageAsync<TradeStatsParsedPage<TRow>>(
                spec, method, query, secId, operationTags,
                parsePage, static parsed => parsed.HttpSourceRowsCount, cancellationToken);
            TradeStatsParsedPage<TRow> parsed = page.Rows;
            PaginationCursorDTO cursor = page.Cursor;

            pagesElapsed++;
            totalRows = checked(totalRows + parsed.AcceptedRowsCount);
            MoexLogMessages.PageReceived(
                _logger, method, pagesElapsed, parsed.AcceptedRowsCount, page.Elapsed);

            if (cursor.Index is null || cursor.PageSize is null || cursor.Total is null)
            {
                throw new InvalidOperationException(
                    "Источник не вернул курсор целиком (INDEX, TOTAL, PAGESIZE): " +
                    "полнота диапазона не доказана.");
            }

            int cursorIndex = cursor.Index.Value;
            int cursorTotal = cursor.Total.Value;
            int cursorPageSize = cursor.PageSize.Value;
            if (cursorIndex < 0 || cursorTotal < 0)
            {
                throw new InvalidOperationException(
                    $"Курсор источника отрицателен: INDEX={cursorIndex}, TOTAL={cursorTotal}.");
            }

            if (cursorPageSize <= 0)
                throw new InvalidOperationException(
                    $"Размер страницы источника не положителен: PAGESIZE={cursorPageSize}.");

            if (cursorIndex > cursorTotal)
            {
                throw new InvalidOperationException(
                    $"Курсор указывает за пределы набора: INDEX={cursorIndex}, TOTAL={cursorTotal}.");
            }

            long nextCursor = (long)cursorIndex + cursorPageSize;
            if (nextCursor >= cursorTotal)
            {
                AccountSkippedRows(parsed, method, secId, operationTags, query);
                yield return parsed;
                MoexLogMessages.PaginationStopped(
                    _logger, method, "range_exhausted", pagesElapsed, totalRows);
                break;
            }

            if (pagesElapsed >= _options.MaxPagesPerLoad)
            {
                throw new InvalidOperationException(
                    $"Достигнут защитный предел Moex:MaxPagesPerLoad={_options.MaxPagesPerLoad} " +
                    "при неисчерпанном диапазоне.");
            }

            AccountSkippedRows(parsed, method, secId, operationTags, query);
            yield return parsed;
            query["start"] = ((int)nextCursor).ToString(CultureInfo.InvariantCulture);
        }
    }

    private async IAsyncEnumerable<HistoricalSeriesParsedPage> ReadTypedCursorPages<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> writer,
        DateTime? lastSourceTime,
        HistoricalPageParser<TRow> parsePageBody,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRow : struct
    {
        if (spec.Pagination != PaginationKind.Cursor)
            throw new ArgumentException("Typed historical cursor pages require cursor pagination.", nameof(spec));

        string method = string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        Dictionary<string, string> query = new()
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["till"] = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["iss.meta"] = "off",
            ["iss.only"] = "data,data.cursor",
            ["data.columns"] = spec.ColumnsParam,
        };

        HistoricalSeriesTimeGroupTail<TRow>? pageTail = null;
        PageParser<HistoricalSeriesParsedPage> parsePage =
            (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
                parsePageBody(body, spec, secId, writer, lastSourceTime, out cursor, out pageTail);

        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageTail = null;
            PageFetchResult<HistoricalSeriesParsedPage> fetched = await FetchPageAsync(
                spec, method, query, secId, operationTags, parsePage,
                parsed => checked(parsed.ParsedValidRowsCount + (pageTail?.PendingParsedValidRowsCount ?? 0)),
                cancellationToken);
            HistoricalSeriesParsedPage parsed = fetched.Rows;
            PaginationCursorDTO cursor = fetched.Cursor;
            int pageAcceptedRows = checked(parsed.ParsedValidRowsCount + (pageTail?.PendingParsedValidRowsCount ?? 0));

            pagesElapsed++;
            totalRows = checked(totalRows + pageAcceptedRows);
            MoexLogMessages.PageReceived(_logger, method, pagesElapsed, pageAcceptedRows, fetched.Elapsed);

            if (cursor.Index is null || cursor.PageSize is null || cursor.Total is null)
                throw new InvalidOperationException(
                    "Источник не вернул курсор целиком (INDEX, TOTAL, PAGESIZE): полнота диапазона не доказана.");

            int cursorIndex = cursor.Index.Value;
            int cursorTotal = cursor.Total.Value;
            int cursorPageSize = cursor.PageSize.Value;
            if (cursorIndex < 0 || cursorTotal < 0)
                throw new InvalidOperationException($"Курсор источника отрицателен: INDEX={cursorIndex}, TOTAL={cursorTotal}.");
            if (cursorPageSize <= 0)
                throw new InvalidOperationException($"Размер страницы источника не положителен: PAGESIZE={cursorPageSize}.");
            if (cursorIndex > cursorTotal)
                throw new InvalidOperationException($"Курсор указывает за пределы набора: INDEX={cursorIndex}, TOTAL={cursorTotal}.");

            long nextCursor = (long)cursorIndex + cursorPageSize;
            if (nextCursor >= cursorTotal)
            {
                if (pageTail is not null)
                    parsed = pageTail.CompletePage();
                AccountSkippedRows(parsed, method, secId, operationTags, query);
                yield return parsed;
                MoexLogMessages.PaginationStopped(_logger, method, "range_exhausted", pagesElapsed, totalRows);
                break;
            }

            if (pagesElapsed >= _options.MaxPagesPerLoad)
                throw new InvalidOperationException(
                    $"Достигнут защитный предел Moex:MaxPagesPerLoad={_options.MaxPagesPerLoad} при неисчерпанном диапазоне.");

            int nextStart = checked((int)nextCursor);
            if (pageTail is { PendingSourceRowsCount: > 0 } && pageTail.FullSourceRowsCount > 0)
            {
                int sourceTailStart = pageTail.StableSourceRowsCount;
                if (sourceTailStart == 0)
                {
                    throw new InvalidOperationException(
                        $"Страница {method} целиком занята одной временной группой "
                        + $"({pageTail.FullSourceRowsCount} строк источника), продолжение чтения не гарантирует полноты.");
                }

                nextStart = checked(cursorIndex + sourceTailStart);
            }

            AccountSkippedRows(parsed, method, secId, operationTags, query);
            pageTail = null;
            yield return parsed;
            query["start"] = nextStart.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async IAsyncEnumerable<HistoricalSeriesParsedPage> ReadTypedDaySplitPages<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> writer,
        DateTime? lastSourceTime,
        HistoricalPageParser<TRow> parsePageBody,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRow : struct
    {
        if (spec.Pagination != PaginationKind.DaySplit)
            throw new ArgumentException("Typed historical day-split pages require day-split pagination.", nameof(spec));

        string method = string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        Dictionary<string, string> query = new()
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["till"] = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["iss.meta"] = "off",
        };
        HistoricalSeriesTimeGroupTail<TRow>? pageTail = null;
        PageParser<HistoricalSeriesParsedPage> parsePage =
            (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
                parsePageBody(body, spec, secId, writer, lastSourceTime, out cursor, out pageTail);

        int dayCount = 0;
        int totalRows = 0;
        DateOnly current = from;
        while (current <= till)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string date = current.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            query["from"] = date;
            query["till"] = date;
            pageTail = null;

            PageFetchResult<HistoricalSeriesParsedPage> fetched = default;
            Exception? fetchFailure = null;
            try
            {
                fetched = await FetchPageAsync(
                    spec, method, query, secId, operationTags, parsePage,
                    static parsed => parsed.ParsedValidRowsCount, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableReadFailure(ex))
            {
                fetchFailure = ex;
            }

            if (fetchFailure is not null)
            {
                yield return new HistoricalSeriesParsedPage(
                    0, 0, null, null,
                    new SeriesReadFailure(current, current, fetchFailure.Message));
                current = current.AddDays(1);
                dayCount++;
                continue;
            }

            HistoricalSeriesParsedPage parsed = fetched.Rows;
            if (parsed.SourceRowsCount > 0)
                AccountSkippedRows(parsed, method, secId, operationTags, query);

            dayCount++;
            totalRows = checked(totalRows + parsed.ParsedValidRowsCount);
            MoexLogMessages.DaySplitPageReceived(_logger, method, date, parsed.ParsedValidRowsCount, fetched.Elapsed);
            if (parsed.SourceRowsCount > 0)
                yield return parsed;

            current = current.AddDays(1);
        }

        MoexLogMessages.DaySplitCompleted(
            _logger, method,
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dayCount, totalRows);
    }

    private void AccountSkippedRows(
        CandleParsedPage parsed,
        string method,
        string secId,
        MoexOperationTags operationTags,
        Dictionary<string, string> query)
    {
        AccountSkippedRows(
            parsed.SkippedSourceRows,
            parsed.SkippedRows,
            method,
            secId,
            operationTags,
            query);
    }

    private void AccountSkippedRows(
        HistoricalSeriesParsedPage parsed,
        string method,
        string secId,
        MoexOperationTags operationTags,
        Dictionary<string, string> query)
    {
        AccountSkippedRows(
            parsed.SkippedSourceRows,
            parsed.SkippedRows,
            method,
            secId,
            operationTags,
            query);
    }

    private void AccountSkippedRows<TRow>(
        TradeStatsParsedPage<TRow> parsed,
        string method,
        string secId,
        MoexOperationTags operationTags,
        Dictionary<string, string> query)
        where TRow : struct
    {
        AccountSkippedRows(
            parsed.SkippedSourceRows,
            parsed.SkippedRows,
            method,
            secId,
            operationTags,
            query);
    }

    private void AccountSkippedRows(
        List<SkippedSourceRow>? skippedSourceRows,
        int skippedRows,
        string method,
        string secId,
        MoexOperationTags operationTags,
        Dictionary<string, string> query)
    {
        if (skippedRows == 0)
            return;

        MoexLogMessages.RowsSkipped(_logger, method, skippedRows);
        for (int i = 0; i < skippedSourceRows!.Count; i++)
        {
            SkippedSourceRow skipped = skippedSourceRows[i];
            MoexLogMessages.HistoryRowSkipped(
                _logger,
                secId,
                operationTags.DataKind,
                operationTags.Operation,
                method,
                query.GetValueOrDefault("from"),
                query.GetValueOrDefault("till"),
                GetPagePosition(query),
                skipped.SourceIndex,
                skipped.Time,
                skipped.Reason);
        }
        MoexMetrics.RowsSkipped.Add(
            skippedRows,
            new KeyValuePair<string, object?>(
                MoexTelemetryAttributes.Source, operationTags.Source),
            new KeyValuePair<string, object?>(
                MoexTelemetryAttributes.DataKind, operationTags.DataKind),
            new KeyValuePair<string, object?>(
                MoexTelemetryAttributes.Market, operationTags.Market));
    }

    private static string GetPagePosition(Dictionary<string, string> query)
    {
        if (query.TryGetValue("start", out string? start))
            return "start=" + start;

        string? from = query.GetValueOrDefault("from");
        string? till = query.GetValueOrDefault("till");
        if (from is not null && from == till)
            return "date=" + from;

        return "start=0";
    }

    internal static bool IsRecoverableReadFailure(Exception exception)
    {
        if (exception is OperationCanceledException)
            return false;

        if (exception is MoexTimeoutException)
            return true;

        if (exception is HttpRequestException requestException)
        {
            return requestException.HttpRequestError is
                HttpRequestError.ConnectionError
                or HttpRequestError.NameResolutionError
                or HttpRequestError.ResponseEnded;
        }

        return exception is MoexHttpStatusException statusException
            && statusException.ErrorCategory is MoexErrorTypes.ServerError or MoexErrorTypes.RateLimit;
    }

    private async Task<PageFetchResult<TPage>> FetchPageAsync<TPage>(
        MoexSeriesSpec spec,
        string method,
        Dictionary<string, string> query,
        string secId,
        MoexOperationTags operationTags,
        PageParser<TPage> parsePage,
        Func<TPage, int> getDiagnosticRowCount,
        CancellationToken cancellationToken)
    {
        long pageStart = Stopwatch.GetTimestamp();

        TPage parsed;
        PaginationCursorDTO cursor;
        TimeSpan elapsed;
        using (Activity? pageActivity =
               MoexTelemetry.ActivitySource.StartActivity("moex.history.fetch"))
        {
            pageActivity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
            pageActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
            pageActivity?.SetTag(MoexTelemetryAttributes.Market, spec.Market);

            try
            {
                using HttpResponseMessage response = await _client.SendRequestAsync(
                    method, query, cancellationToken, operationTags.Flow);
                using RentedBuffer body = await RentedBuffer.RentFromResponseAsync(
                    response, _options.BodyReadTimeout, method, cancellationToken);

                try
                {
                    parsed = parsePage(body.Memory, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(
                        _logger, ex, method, MoexErrorTypes.SchemaMismatch, ex.Message);
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                pageActivity?.SetStatus(ActivityStatusCode.Ok);
                MoexMetrics.RecordOperationCancelled(
                    in operationTags,
                    Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
                throw;
            }
            catch (Exception ex)
            {
                string pagePosition = query.TryGetValue("start", out string? start)
                    ? "start=" + start
                    : query.TryGetValue("from", out string? date)
                        ? "date=" + date
                        : "initial";
                MoexLogMessages.HistoryOperationFailed(
                    _logger,
                    ex,
                    secId,
                    operationTags.DataKind,
                    operationTags.Operation,
                    method,
                    query.GetValueOrDefault("from"),
                    query.GetValueOrDefault("till"),
                    pagePosition,
                    MoexMetrics.ClassifyError(ex),
                    ex.Message);
                pageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                MoexMetrics.RecordOperationError(
                    in operationTags,
                    ex,
                    Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
                throw;
            }

            // Время страницы фиксируется здесь — в той же точке, где сегодня вызывается
            // запись в журнал, то есть до метрик. Иначе в журнал попадала бы длительность,
            // включающая запись метрик, и смысл поля изменился бы.
            elapsed = Stopwatch.GetElapsedTime(pageStart);

            MoexMetrics.PagesReceived.Add(
                1,
                new KeyValuePair<string, object?>(
                    MoexTelemetryAttributes.Source, operationTags.Source),
                new KeyValuePair<string, object?>(
                    MoexTelemetryAttributes.DataKind, operationTags.DataKind),
                new KeyValuePair<string, object?>(
                    MoexTelemetryAttributes.Market, operationTags.Market));

            TagList rowsTags = new TagList
            {
                { MoexTelemetryAttributes.Source, operationTags.Source },
                { MoexTelemetryAttributes.DataKind, operationTags.DataKind },
                { MoexTelemetryAttributes.Market, operationTags.Market },
                { MoexTelemetryAttributes.Flow, operationTags.Flow },
            };
            MoexMetrics.RowsReceived.Add(getDiagnosticRowCount(parsed), rowsTags);

            MoexMetrics.RecordOperationSuccess(
                in operationTags,
                Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            pageActivity?.SetStatus(ActivityStatusCode.Ok);
        }

        return new PageFetchResult<TPage>(parsed, cursor, elapsed);
    }
}
