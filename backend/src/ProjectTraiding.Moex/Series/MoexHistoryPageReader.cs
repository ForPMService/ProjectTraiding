using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
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

    internal delegate Task ReadSeriesPages<TRow>(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till, MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> writer, DateTime? lastSourceTime, HistoricalReadState state,
        Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        where TRow : struct;

    internal delegate Task ReadTradeStatsPages<TRow>(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till, MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> statsWriter, ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime, uint ingestPriority, HistoricalReadState state,
        List<CandleRow>? acceptedCandles, Action? beforeRequest,
        BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        where TRow : struct;

    private delegate PaginationCursorDTO ParseSeriesBody<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ReadOnlyMemory<byte> body,
        ref HistoricalSeriesTimeGroupTail<TRow>? timeGroupTail) where TRow : struct;
    internal delegate ValueTask BetweenHistoryPages(CancellationToken cancellationToken);

    public MoexHistoryPageReader(
        MoexHttpAlgClient client,
        IOptions<MoexOptions> options,
        ILogger<MoexHistoryPageReader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    internal Task ReadTradeStatsStockPagesAsync(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till, MoexOperationTags operationTags,
        ProtobufBatchWriter<TradeStatsStockRow> statsWriter, ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime, uint ingestPriority, HistoricalReadState state,
        List<CandleRow>? acceptedCandles, Action? beforeRequest,
        BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
    {
        Func<ReadOnlyMemory<byte>, PaginationCursorDTO> parse = body =>
        {
            TradeStatsHistoryParser.ParseStock(body.Span, spec, secId, statsWriter, candleWriter,
                lastSourceTime, ingestPriority, state, out PaginationCursorDTO cursor, acceptedCandles);
            return cursor;
        };
        return ReadTradeStatsCursorPagesAsync(spec, secId, from, till, operationTags, state,
            parse, beforeRequest, afterPage, cancellationToken);
    }

    internal Task ReadTradeStatsFuturesPagesAsync(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till, MoexOperationTags operationTags,
        ProtobufBatchWriter<TradeStatsFuturesRow> statsWriter, ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime, uint ingestPriority, HistoricalReadState state,
        List<CandleRow>? acceptedCandles, Action? beforeRequest,
        BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
    {
        Func<ReadOnlyMemory<byte>, PaginationCursorDTO> parse = body =>
        {
            TradeStatsHistoryParser.ParseFutures(body.Span, spec, secId, statsWriter, candleWriter,
                lastSourceTime, ingestPriority, state, out PaginationCursorDTO cursor, acceptedCandles);
            return cursor;
        };
        return ReadTradeStatsCursorPagesAsync(spec, secId, from, till, operationTags, state,
            parse, beforeRequest, afterPage, cancellationToken);
    }

    internal Task ReadObStatsStockPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<ObStatsStockRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedCursorPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<ObStatsStockRow>? tail) =>
            {
                ObStatsHistoryParser.ParseStock(body.Span, spec, secId, writer, lastSourceTime, state,
                    out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadObStatsFuturesPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<ObStatsFuturesRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedCursorPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<ObStatsFuturesRow>? tail) =>
            {
                ObStatsHistoryParser.ParseFutures(body.Span, spec, secId, writer, lastSourceTime, state,
                    out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadOrderStatsStockPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<OrderStatsStockRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedCursorPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<OrderStatsStockRow>? tail) =>
            {
                OrderStatsHistoryParser.Parse(body.Span, spec, secId, writer, lastSourceTime, state,
                    out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadFutoiPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<FutoiRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedDaySplitPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<FutoiRow>? tail) =>
            {
                FutoiHistoryParser.Parse(body.Span, spec, secId, writer, lastSourceTime, state,
                    out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadHi2StockPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<Hi2StockRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedCursorPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<Hi2StockRow>? tail) =>
            {
                Hi2HistoryParser.ParseStock(body.Span, spec, secId, writer, lastSourceTime, state,
                    ref tail, out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadHi2FuturesPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<Hi2FuturesRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedCursorPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<Hi2FuturesRow>? tail) =>
            {
                Hi2HistoryParser.ParseFutures(body.Span, spec, secId, writer, lastSourceTime, state,
                    ref tail, out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadMegaAlertsStockPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<MegaAlertsStockRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedCursorPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<MegaAlertsStockRow>? tail) =>
            {
                MegaAlertsHistoryParser.ParseStock(body.Span, spec, secId, writer, lastSourceTime, state,
                    ref tail, out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadMegaAlertsFuturesPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<MegaAlertsFuturesRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        => ReadTypedCursorPagesAsync(spec, secId, from, till, operationTags, state, beforeRequest,
            (ReadOnlyMemory<byte> body, ref HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow>? tail) =>
            {
                MegaAlertsHistoryParser.ParseFutures(body.Span, spec, secId, writer, lastSourceTime, state,
                    ref tail, out PaginationCursorDTO cursor);
                return cursor;
            }, afterPage, cancellationToken);

    internal Task ReadCandlePagesAsync(
        MoexSeriesSpec spec, string secId, string boardId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<CandleRow> writer,
        HistoricalReadState state, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
    {
        if (spec.DataKind != "candles" || spec.Pagination != PaginationKind.FixedPage)
            throw new ArgumentException("Typed candle reads require a fixed-page candle specification.", nameof(spec));
        if (spec.CandleInterval is not int interval)
            throw new InvalidOperationException($"Декларация {spec.DataKind}/{spec.Market} не содержит интервал свечей.");

        string board = spec.Market == "stock" ? boardId.ToLowerInvariant() : boardId.ToUpperInvariant();
        string method = string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId, board);
        Dictionary<string, string> query = new()
        {
            ["from"] = FormatDate(from),
            ["till"] = FormatDate(till),
            ["interval"] = interval.ToString(CultureInfo.InvariantCulture),
            ["iss.meta"] = "off",
            ["iss.only"] = spec.RootKey,
            ["candles.columns"] = MoexColumnSets.CandlesColumnsParam,
        };

        return ReadFixedCandlePagesAsync(spec, secId, from, till, operationTags,
            method, query, writer, state, afterPage, cancellationToken);
    }

    private async Task ReadFixedCandlePagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, string method, Dictionary<string, string> query,
        ProtobufBatchWriter<CandleRow> writer, HistoricalReadState state,
        BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
    {
        int queryStart = 0;
        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.BeginPage(method, from, till, "start=" + queryStart.ToString(CultureInfo.InvariantCulture));
            int sourceRowsCount = await FetchPageAsync(
                method, query, secId, operationTags, state,
                body => CandleHistoryParser.Parse(body.Span, spec, secId, writer, state), cancellationToken);
            pagesElapsed++;
            totalRows = checked(totalRows + state.PageAcceptedRows);
            MoexLogMessages.PageReceived(_logger, method, pagesElapsed, state.PageAcceptedRows, state.LastPageElapsed);
            state.RecordSkippedDiagnostics();

            if (afterPage is not null)
                await afterPage(cancellationToken);

            if (sourceRowsCount >= FixedPageSize)
            {
                if (pagesElapsed >= _options.MaxPagesPerLoad)
                    throw new InvalidOperationException(
                        $"Достигнут защитный предел Moex:MaxPagesPerLoad={_options.MaxPagesPerLoad} при неисчерпанном диапазоне.");
                queryStart = checked(queryStart + FixedPageSize);
                query["start"] = queryStart.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                MoexLogMessages.FixedPagePaginationStopped(
                    _logger, method, "last_page_incomplete", pagesElapsed, totalRows,
                    state.PageAcceptedRows, FixedPageSize);
                break;
            }
        }
    }

    private async Task ReadTradeStatsCursorPagesAsync(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till, MoexOperationTags operationTags,
        HistoricalReadState state, Func<ReadOnlyMemory<byte>, PaginationCursorDTO> parse, Action? beforeRequest,
        BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
    {
        if (spec.DataKind != "tradestats" || spec.Pagination != PaginationKind.Cursor)
            throw new ArgumentException("Typed TradeStats reads require cursor pagination.", nameof(spec));
        string method = string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        Dictionary<string, string> query = CreateCursorQuery(spec, from, till);
        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.BeginPage(method, from, till, GetPagePosition(query));
            beforeRequest?.Invoke();
            PaginationCursorDTO cursor = await FetchPageAsync(method, query, secId, operationTags,
                state, parse, cancellationToken);
            pagesElapsed++;
            totalRows = checked(totalRows + state.PageAcceptedRows);
            MoexLogMessages.PageReceived(_logger, method, pagesElapsed, state.PageAcceptedRows, state.LastPageElapsed);
            ValidateCursor(cursor, method);
            long nextCursor = GetNextCursor(cursor);
            bool exhausted = nextCursor >= cursor.Total!.Value;
            if (exhausted)
            {
                state.RecordSkippedDiagnostics();
                if (afterPage is not null)
                    await afterPage(cancellationToken);
                MoexLogMessages.PaginationStopped(_logger, method, "range_exhausted", pagesElapsed, totalRows);
                break;
            }
            if (pagesElapsed >= _options.MaxPagesPerLoad)
                throw PageLimitExceeded();
            state.RecordSkippedDiagnostics();
            if (afterPage is not null)
                await afterPage(cancellationToken);
            query["start"] = checked((int)nextCursor).ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task ReadTypedCursorPagesAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till, MoexOperationTags operationTags,
        HistoricalReadState state, Action? beforeRequest,
        ParseSeriesBody<TRow> parse, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        where TRow : struct
    {
        if (spec.Pagination != PaginationKind.Cursor)
            throw new ArgumentException("Typed historical series require cursor pagination.", nameof(spec));
        string method = string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        Dictionary<string, string> query = CreateCursorQuery(spec, from, till);
        HistoricalSeriesTimeGroupTail<TRow>? tail = null;
        HistoricalSeriesTimeGroupTail<TRow>? previousTail = null;
        int pagesElapsed = 0;
        int totalRows = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.BeginPage(method, from, till, GetPagePosition(query));
                previousTail = tail;
                tail = null;
                beforeRequest?.Invoke();
                PaginationCursorDTO cursor = await FetchPageAsync(method, query, secId, operationTags, state,
                    body =>
                    {
                        PaginationCursorDTO pageCursor = parse(body, ref tail);
                        state.RecordRowsReceivedMetric(tail?.PendingParsedValidRowsCount ?? 0);
                        return pageCursor;
                    }, cancellationToken);
                int pageRows = checked(state.PageParsedValidRows + (tail?.PendingParsedValidRowsCount ?? 0));
                pagesElapsed++;
                totalRows = checked(totalRows + pageRows);
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, pageRows, state.LastPageElapsed);
                ValidateCursor(cursor, method);
                long nextCursor = GetNextCursor(cursor);
                bool exhausted = nextCursor >= cursor.Total!.Value;
                if (exhausted)
                {
                    tail?.CommitComplete();
                    state.RecordSkippedDiagnostics();
                    if (afterPage is not null)
                        await afterPage(cancellationToken);
                    MoexLogMessages.PaginationStopped(_logger, method, "range_exhausted", pagesElapsed, totalRows);
                    break;
                }
                if (pagesElapsed >= _options.MaxPagesPerLoad)
                    throw PageLimitExceeded();

                int nextStart = checked((int)nextCursor);
                if (tail is { PendingSourceRowsCount: > 0, FullSourceRowsCount: > 0 })
                {
                    int stableSourceRows = tail.StableSourceRowsCount;
                    if (stableSourceRows == 0)
                        throw new InvalidOperationException(
                            $"Страница {method} целиком занята одной временной группой " +
                            $"({tail.FullSourceRowsCount} строк источника), продолжение чтения не гарантирует полноты.");
                    nextStart = checked(cursor.Index!.Value + stableSourceRows);
                }

                state.RecordSkippedDiagnostics();
                if (afterPage is not null)
                    await afterPage(cancellationToken);
                query["start"] = nextStart.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            int reparsedRows = tail?.FullSourceRowsCount ?? 0;
            tail?.RecordPendingSkipsFrom();
            previousTail?.RecordPendingSkipsFrom(reparsedRows);
            state.RecordSkippedDiagnostics();
            throw;
        }
    }

    private async Task ReadTypedDaySplitPagesAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till, MoexOperationTags operationTags,
        HistoricalReadState state, Action? beforeRequest,
        ParseSeriesBody<TRow> parse, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
        where TRow : struct
    {
        if (spec.Pagination != PaginationKind.DaySplit)
            throw new ArgumentException("Typed historical series require day-split pagination.", nameof(spec));
        string method = string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        Dictionary<string, string> query = new()
        {
            ["from"] = FormatDate(from),
            ["till"] = FormatDate(from),
            ["iss.meta"] = "off",
        };
        int dayCount = 0;
        int totalRows = 0;
        DateOnly current = from;
        while (current <= till)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string date = FormatDate(current);
            query["from"] = date;
            query["till"] = date;
            state.BeginPage(method, current, current, "date=" + date);
            beforeRequest?.Invoke();
            HistoricalSeriesTimeGroupTail<TRow>? tail = null;
            try
            {
                _ = await FetchPageAsync(method, query, secId, operationTags, state,
                    body => parse(body, ref tail), cancellationToken);
                state.RecordSkippedDiagnostics();
                dayCount++;
                totalRows = checked(totalRows + state.PageParsedValidRows);
                MoexLogMessages.DaySplitPageReceived(_logger, method, date, state.PageParsedValidRows, state.LastPageElapsed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableReadFailure(ex))
            {
                state.RecordReadFailure(current, current, ex.Message);
                dayCount++;
            }
            if (afterPage is not null)
                await afterPage(cancellationToken);
            current = current.AddDays(1);
        }
        MoexLogMessages.DaySplitCompleted(_logger, method, FormatDate(from), FormatDate(till), dayCount, totalRows);
    }

    private async Task<TResult> FetchPageAsync<TResult>(
        string method, Dictionary<string, string> query, string secId, MoexOperationTags operationTags,
        HistoricalReadState state, Func<ReadOnlyMemory<byte>, TResult> parse,
        CancellationToken cancellationToken)
    {
        long pageStart = Stopwatch.GetTimestamp();
        bool parserStarted = false;
        bool parseSucceeded = false;
        using Activity? pageActivity = MoexTelemetry.ActivitySource.StartActivity("moex.history.fetch");
        pageActivity?.SetTag(MoexTelemetryAttributes.Source, operationTags.Source);
        pageActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
        pageActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
        try
        {
            using HttpResponseMessage response = await _client.SendRequestAsync(method, query, cancellationToken, operationTags.Flow);
            using RentedBuffer body = await RentedBuffer.RentFromResponseAsync(response, _options.BodyReadTimeout, method, cancellationToken);
            parserStarted = true;
            TResult result = parse(body.Memory);
            parseSucceeded = true;
            state.LastPageElapsed = Stopwatch.GetElapsedTime(pageStart);
            pageActivity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (OperationCanceledException)
        {
            pageActivity?.SetStatus(ActivityStatusCode.Ok);
            MoexMetrics.RecordOperationCancelled(in operationTags, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            throw;
        }
        catch (MoexSchemaMismatchException ex)
        {
            MoexLogMessages.ParseFailed(_logger, ex, method, MoexErrorTypes.SchemaMismatch, ex.Message);
            MoexLogMessages.HistoryOperationFailed(
                _logger, ex, secId, operationTags.DataKind, operationTags.Operation,
                method, state.PageFrom, state.PageTill, state.PagePosition,
                MoexErrorTypes.SchemaMismatch, ex.Message);
            pageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            MoexMetrics.RecordOperationError(in operationTags, ex, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            throw;
        }
        catch (Exception ex)
        {
            MoexLogMessages.HistoryOperationFailed(
                _logger, ex, secId, operationTags.DataKind, operationTags.Operation,
                method, state.PageFrom, state.PageTill, state.PagePosition,
                MoexMetrics.ClassifyError(ex), ex.Message);
            pageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            MoexMetrics.RecordOperationError(in operationTags, ex, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            throw;
        }
        finally
        {
            if (parserStarted && parseSucceeded)
                RecordPageMetrics(operationTags, state);
            else if (parserStarted)
                state.RecordSkippedDiagnostics();
        }
    }

    private void RecordPageMetrics(MoexOperationTags operationTags, HistoricalReadState state)
    {
        MoexMetrics.PagesReceived.Add(
            1,
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, operationTags.Source),
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, operationTags.DataKind),
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, operationTags.Market));
        state.RecordReceivedMetrics();
        state.RecordSkippedDiagnostics();
        MoexMetrics.RecordOperationSuccess(in operationTags, state.LastPageElapsed.TotalSeconds);
    }

    private static Dictionary<string, string> CreateCursorQuery(MoexSeriesSpec spec, DateOnly from, DateOnly till)
        => new()
        {
            ["from"] = FormatDate(from),
            ["till"] = FormatDate(till),
            ["iss.meta"] = "off",
            ["iss.only"] = "data,data.cursor",
            ["data.columns"] = spec.ColumnsParam,
        };

    private static void ValidateCursor(PaginationCursorDTO cursor, string method)
    {
        if (cursor.Index is null || cursor.PageSize is null || cursor.Total is null)
            throw new InvalidOperationException("Источник не вернул курсор целиком (INDEX, TOTAL, PAGESIZE): полнота диапазона не доказана.");
        int index = cursor.Index.Value;
        int total = cursor.Total.Value;
        int pageSize = cursor.PageSize.Value;
        if (index < 0 || total < 0)
            throw new InvalidOperationException($"Курсор источника отрицателен: INDEX={index}, TOTAL={total}.");
        if (pageSize <= 0)
            throw new InvalidOperationException($"Размер страницы источника не положителен: PAGESIZE={pageSize}.");
        if (index > total)
            throw new InvalidOperationException($"Курсор указывает за пределы набора {method}: INDEX={index}, TOTAL={total}.");
        _ = checked((long)index + pageSize);
    }

    private static long GetNextCursor(PaginationCursorDTO cursor)
        => checked((long)cursor.Index!.Value + cursor.PageSize!.Value);

    private static InvalidOperationException PageLimitExceeded()
        => new($"Достигнут защитный предел Moex:MaxPagesPerLoad при неисчерпанном диапазоне.");

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string GetPagePosition(Dictionary<string, string> query)
        => query.TryGetValue("start", out string? start) ? "start=" + start : "start=0";

    internal static bool IsRecoverableReadFailure(Exception exception)
    {
        if (exception is OperationCanceledException) return false;
        if (exception is MoexTimeoutException) return true;
        if (exception is HttpRequestException requestException)
            return requestException.HttpRequestError is HttpRequestError.ConnectionError
                or HttpRequestError.NameResolutionError or HttpRequestError.ResponseEnded;
        return exception is MoexHttpStatusException statusException
            && statusException.ErrorCategory is MoexErrorTypes.ServerError or MoexErrorTypes.RateLimit;
    }
}
