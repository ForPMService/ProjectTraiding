using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Pagination;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
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

    private static ImmutableArray<string> ColumnsFor<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ProtobufBatchWriter<TRow> writer)
        where TRow : struct
        => writer switch
        {
            ProtobufBatchWriter<TradeStatsStockRow> => MoexColumnSets.TradeStatsStock,
            ProtobufBatchWriter<TradeStatsFuturesRow> => MoexColumnSets.TradeStatsFutures,
            ProtobufBatchWriter<ObStatsStockRow> => MoexColumnSets.ObStatsStock,
            ProtobufBatchWriter<ObStatsFuturesRow> => MoexColumnSets.ObStatsFutures,
            ProtobufBatchWriter<OrderStatsStockRow> => MoexColumnSets.OrderStatsStock,
            ProtobufBatchWriter<Hi2StockRow> => MoexColumnSets.Hi2Stock,
            ProtobufBatchWriter<Hi2FuturesRow> => MoexColumnSets.Hi2Futures,
            ProtobufBatchWriter<MegaAlertsStockRow> => MoexColumnSets.MegaAlertsStock,
            ProtobufBatchWriter<MegaAlertsFuturesRow> => MoexColumnSets.MegaAlertsFutures,
            _ => throw new NotSupportedException($"Тип строки {typeof(TRow).Name} ещё не переведён."),
        };

    internal async Task ReadPagesAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec, string secId, string? boardId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags,
        ProtobufBatchWriter<TRow> writer, ProtobufBatchWriter<CandleRow>? candleWriter,
        DateTime? lastSourceTime, uint ingestPriority, List<CandleRow>? acceptedCandles,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage,
        CancellationToken cancellationToken)
        where TRow : struct
    {
        if (spec.InputColumns != ColumnsFor(writer))
            throw new ArgumentException("Декларация серии не соответствует типу строки.", nameof(spec));

        string method = BuildMethod(spec, secId, boardId);
        Dictionary<string, string> query = CreateQuery(spec, from, till);
        int start = 0;
        int pages = 0;
        int totalRows = 0;
        DateOnly day = from;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool daySplit = spec.Pagination == PaginationKind.DaySplit;
            DateOnly pageFrom = daySplit ? day : from;
            DateOnly pageTill = daySplit ? day : till;
            string pagePosition = daySplit
                ? "date=" + FormatDate(day)
                : "start=" + start.ToString(CultureInfo.InvariantCulture);
            state.BeginPage(method, pageFrom, pageTill, pagePosition);
            beforeRequest?.Invoke();

            int sourceRows = 0;
            PaginationCursorDTO cursor = default;
            try
            {
                (int SourceRows, PaginationCursorDTO Cursor) page = await ReadPageAsync(
                    method, query, secId, operationTags, state, spec, writer, candleWriter,
                    lastSourceTime, ingestPriority, acceptedCandles, cancellationToken);
                sourceRows = page.SourceRows;
                cursor = page.Cursor;
            }
            catch (Exception exception) when (
                spec.Pagination == PaginationKind.DaySplit && IsRecoverableReadFailure(exception))
            {
                state.RecordReadFailure(day, day, exception.Message);
            }

            pages++;
            totalRows = checked(totalRows + state.PageAcceptedRows);
            MoexLogMessages.PageReceived(
                _logger, method, pages, state.PageAcceptedRows, state.LastPageElapsed);

            bool hasNext = TryNextPage(
                spec, cursor, sourceRows, ref start, ref day, till, pages, query, method);
            state.RecordSkippedDiagnostics();
            if (afterPage is not null)
                await afterPage(cancellationToken);
            if (!hasNext)
            {
                MoexLogMessages.PaginationStopped(_logger, method, "range_exhausted", pages, totalRows);
                break;
            }
        }
    }

    private static string BuildMethod(MoexSeriesSpec spec, string secId, string? boardId)
    {
        if (boardId is null)
            return string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        string board = spec.Market == "stock" ? boardId.ToLowerInvariant() : boardId.ToUpperInvariant();
        return string.Format(CultureInfo.InvariantCulture, spec.MethodTemplate, secId, board);
    }

    private static Dictionary<string, string> CreateQuery(MoexSeriesSpec spec, DateOnly from, DateOnly till)
    {
        switch (spec.Pagination)
        {
            case PaginationKind.Cursor:
                return CreateCursorQuery(spec, from, till);
            case PaginationKind.FixedPage:
                if (spec.CandleInterval is not int interval)
                    throw new InvalidOperationException(
                        $"Декларация {spec.DataKind}/{spec.Market} не содержит интервал свечей.");
                return new Dictionary<string, string>
                {
                    ["from"] = FormatDate(from),
                    ["till"] = FormatDate(till),
                    ["interval"] = interval.ToString(CultureInfo.InvariantCulture),
                    ["iss.meta"] = "off",
                    ["iss.only"] = spec.RootKey,
                    [spec.RootKey + ".columns"] = spec.ColumnsParam,
                };
            case PaginationKind.DaySplit:
                return new Dictionary<string, string>
                {
                    ["from"] = FormatDate(from),
                    ["till"] = FormatDate(from),
                    ["iss.meta"] = "off",
                };
            default:
                throw new NotSupportedException($"Неизвестный тип пагинации {spec.Pagination}.");
        }
    }

    private async Task<(int SourceRows, PaginationCursorDTO Cursor)> ReadPageAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        string method, Dictionary<string, string> query, string secId, MoexOperationTags operationTags,
        HistoricalReadState state, MoexSeriesSpec spec, ProtobufBatchWriter<TRow> writer,
        ProtobufBatchWriter<CandleRow>? candleWriter, DateTime? lastSourceTime, uint ingestPriority,
        List<CandleRow>? acceptedCandles, CancellationToken cancellationToken)
        where TRow : struct
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
            using HttpResponseMessage response = await _client.SendRequestAsync(
                method, query, cancellationToken, operationTags.Flow);
            using RentedBuffer body = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, method, cancellationToken);
            parserStarted = true;
            (int SourceRows, PaginationCursorDTO Cursor) result = ParsePage(
                body.Span, spec, secId, writer, candleWriter,
                lastSourceTime, ingestPriority, acceptedCandles, state);
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
        catch (MoexSchemaMismatchException exception)
        {
            MoexLogMessages.ParseFailed(
                _logger, exception, method, MoexErrorTypes.SchemaMismatch, exception.Message);
            MoexLogMessages.HistoryOperationFailed(
                _logger, exception, secId, operationTags.DataKind, operationTags.Operation,
                method, state.PageFrom, state.PageTill, state.PagePosition,
                MoexErrorTypes.SchemaMismatch, exception.Message);
            pageActivity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            MoexMetrics.RecordOperationError(
                in operationTags, exception, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            throw;
        }
        catch (Exception exception)
        {
            MoexLogMessages.HistoryOperationFailed(
                _logger, exception, secId, operationTags.DataKind, operationTags.Operation,
                method, state.PageFrom, state.PageTill, state.PagePosition,
                MoexMetrics.ClassifyError(exception), exception.Message);
            pageActivity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            MoexMetrics.RecordOperationError(
                in operationTags, exception, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
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

    private static (int SourceRows, PaginationCursorDTO Cursor) ParsePage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ReadOnlySpan<byte> body, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<TRow> writer,
        ProtobufBatchWriter<CandleRow>? candleWriter, DateTime? lastSourceTime, uint ingestPriority,
        List<CandleRow>? acceptedCandles, HistoricalReadState state)
        where TRow : struct
    {
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartObject, "объект ответа", spec.RootKey);
        bool foundColumns = false;
        bool foundData = false;
        int sourceRows = 0;
        PaginationCursorDTO cursor = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals(spec.RootKey))
            {
                ParseHelpersUtf8.ReadAndExpect(
                    ref reader, JsonTokenType.StartObject, spec.RootKey, spec.RootKey);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;

                    if (reader.ValueTextEquals("columns"u8))
                    {
                        foundColumns = true;
                        ParseHelpersUtf8.ValidateInputColumnsUtf8(
                            ref reader, spec.InputColumns, spec.RootKey);
                    }
                    else if (reader.ValueTextEquals("data"u8) && foundColumns)
                    {
                        foundData = true;
                        sourceRows = ReadDataRows(
                            ref reader, spec, secId, writer, candleWriter, lastSourceTime,
                            ingestPriority, acceptedCandles, state);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            else if (reader.ValueTextEquals("data.cursor"u8))
            {
                cursor = ParseHelpersUtf8.ReadCursorRootObject(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        return (sourceRows, cursor);
    }

    private static int ReadDataRows<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ref Utf8JsonReader reader, MoexSeriesSpec spec, string secId, ProtobufBatchWriter<TRow> writer,
        ProtobufBatchWriter<CandleRow>? candleWriter, DateTime? lastSourceTime, uint ingestPriority,
        List<CandleRow>? acceptedCandles, HistoricalReadState state)
        where TRow : struct
    {
        ImmutableArray<string> columns = spec.InputColumns;
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);

        int rowIndex = 0;
        for (; reader.Read() && reader.TokenType != JsonTokenType.EndArray; rowIndex++)
        {
            state.RecordPhysicalRow(receivedMetric: true);
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                state.RecordSkipped(rowIndex, null, "Строка data не является массивом.");
                reader.Skip();
                continue;
            }

            int rowDepth = reader.CurrentDepth;
            bool ok;
            bool appended = false;
            DateTime? sourceTime;
            int columnIndex;
            string? error;

            switch (writer)
            {
                case ProtobufBatchWriter<TradeStatsStockRow> stockWriter:
                    ok = TradeStatsHistoryParser.TryReadStockRow(
                        ref reader, columns.Length, out TradeStatsStockRow stockRow,
                        out CandleRow stockCandle, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(stockWriter, in stockRow, sourceTime, lastSourceTime);
                    if (appended)
                        AppendCandle(candleWriter!, ref stockCandle, ingestPriority, acceptedCandles);
                    break;
                case ProtobufBatchWriter<TradeStatsFuturesRow> futuresWriter:
                    ok = TradeStatsHistoryParser.TryReadFuturesRow(
                        ref reader, columns.Length, out TradeStatsFuturesRow futuresRow,
                        out CandleRow futuresCandle, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(futuresWriter, in futuresRow, sourceTime, lastSourceTime);
                    if (appended)
                        AppendCandle(candleWriter!, ref futuresCandle, ingestPriority, acceptedCandles);
                    break;
                case ProtobufBatchWriter<ObStatsStockRow> obStatsStockWriter:
                    ok = ObStatsHistoryParser.TryReadStockRow(ref reader, columns.Length, secId,
                        out ObStatsStockRow obStatsStockRow, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(obStatsStockWriter, in obStatsStockRow, sourceTime, lastSourceTime);
                    break;
                case ProtobufBatchWriter<ObStatsFuturesRow> obStatsFuturesWriter:
                    ok = ObStatsHistoryParser.TryReadFuturesRow(ref reader, columns.Length, secId,
                        out ObStatsFuturesRow obStatsFuturesRow, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(obStatsFuturesWriter, in obStatsFuturesRow, sourceTime, lastSourceTime);
                    break;
                case ProtobufBatchWriter<OrderStatsStockRow> orderStatsWriter:
                    ok = OrderStatsHistoryParser.TryReadRow(ref reader, columns.Length, secId,
                        out OrderStatsStockRow orderStatsRow, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(orderStatsWriter, in orderStatsRow, sourceTime, lastSourceTime);
                    break;
                case ProtobufBatchWriter<Hi2StockRow> hi2StockWriter:
                    ok = Hi2HistoryParser.TryReadStockRow(ref reader, columns.Length, secId,
                        out Hi2StockRow hi2StockRow, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(hi2StockWriter, in hi2StockRow, sourceTime, lastSourceTime);
                    break;
                case ProtobufBatchWriter<Hi2FuturesRow> hi2FuturesWriter:
                    ok = Hi2HistoryParser.TryReadFuturesRow(ref reader, columns.Length, secId,
                        out Hi2FuturesRow hi2FuturesRow, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(hi2FuturesWriter, in hi2FuturesRow, sourceTime, lastSourceTime);
                    break;
                case ProtobufBatchWriter<MegaAlertsStockRow> megaAlertsStockWriter:
                    ok = MegaAlertsHistoryParser.TryReadStockRow(ref reader, columns.Length, secId,
                        out MegaAlertsStockRow megaAlertsStockRow, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(megaAlertsStockWriter, in megaAlertsStockRow, sourceTime, lastSourceTime);
                    break;
                case ProtobufBatchWriter<MegaAlertsFuturesRow> megaAlertsFuturesWriter:
                    ok = MegaAlertsHistoryParser.TryReadFuturesRow(ref reader, columns.Length, secId,
                        out MegaAlertsFuturesRow megaAlertsFuturesRow, out sourceTime, out columnIndex, out error);
                    appended = ok && AppendIfNew(megaAlertsFuturesWriter, in megaAlertsFuturesRow, sourceTime, lastSourceTime);
                    break;
                default:
                    throw new NotSupportedException($"Тип строки {typeof(TRow).Name} ещё не переведён.");
            }

            state.SetPageLastSourceTime(sourceTime);
            if (!ok)
            {
                int? failed = columnIndex > 0 ? columnIndex - 1 : null;
                string? name = failed is int index && index < columns.Length ? columns[index] : null;
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, rowIndex, sourceTime,
                    error ?? "Ошибка разбора строки.", failed, name);
                continue;
            }

            state.RecordParsedValidRow();
            if (appended)
                state.RecordAccepted(sourceTime);
        }
        return rowIndex;
    }

    private bool TryNextPage(
        MoexSeriesSpec spec, PaginationCursorDTO cursor, int sourceRows,
        ref int start, ref DateOnly day, DateOnly till, int pages,
        Dictionary<string, string> query, string method)
    {
        switch (spec.Pagination)
        {
            case PaginationKind.Cursor:
                if (!MoexPagination.TryNextCursor(
                    cursor, start, pages, _options.MaxPagesPerLoad, _logger, method, out int nextStart))
                    return false;
                start = nextStart;
                query["start"] = start.ToString(CultureInfo.InvariantCulture);
                return true;
            case PaginationKind.FixedPage:
                if (!MoexPagination.TryNextFixedPage(
                    start, sourceRows, FixedPageSize, pages, _options.MaxPagesPerLoad,
                    _logger, method, out int nextPageStart))
                    return false;
                start = nextPageStart;
                query["start"] = start.ToString(CultureInfo.InvariantCulture);
                return true;
            case PaginationKind.DaySplit:
                if (!MoexPagination.TryNextDay(day, till, out DateOnly nextDay))
                    return false;
                day = nextDay;
                query["from"] = FormatDate(day);
                query["till"] = FormatDate(day);
                return true;
            default:
                throw new NotSupportedException($"Неизвестный тип пагинации {spec.Pagination}.");
        }
    }

    private static bool IsNew(DateTime? sourceTime, DateTime? lastSourceTime)
        => lastSourceTime is not DateTime last || sourceTime is not DateTime time || time > last;

    private static bool AppendIfNew<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ProtobufBatchWriter<TRow> writer, in TRow row, DateTime? sourceTime, DateTime? lastSourceTime)
        where TRow : struct
    {
        if (!IsNew(sourceTime, lastSourceTime))
            return false;
        ParseHelpersUtf8.AppendRow(writer, in row);
        return true;
    }

    private static void AppendCandle(
        ProtobufBatchWriter<CandleRow> candleWriter, ref CandleRow candle,
        uint ingestPriority, List<CandleRow>? acceptedCandles)
    {
        candle.IngestPriority = ingestPriority;
        ParseHelpersUtf8.AppendRow(candleWriter, in candle);
        acceptedCandles?.Add(candle);
    }

    internal async Task ReadFutoiPagesAsync(MoexSeriesSpec spec, string secId, DateOnly from, DateOnly till,
        MoexOperationTags operationTags, ProtobufBatchWriter<FutoiRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, Action? beforeRequest, BetweenHistoryPages? afterPage, CancellationToken cancellationToken)
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
            try
            {
                _ = await FetchPageAsync(method, query, secId, operationTags, state,
                    body =>
                    {
                        FutoiHistoryParser.Parse(body.Span, spec, secId, writer, lastSourceTime, state);
                        return state.PageParsedValidRows;
                    }, cancellationToken);
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
            if (!MoexPagination.TryNextDay(current, till, out DateOnly nextDay))
                break;
            current = nextDay;
        }
        MoexLogMessages.DaySplitCompleted(_logger, method, FormatDate(from), FormatDate(till), dayCount, totalRows);
    }

    internal async Task ReadCandlePagesAsync(
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

            bool hasNext = MoexPagination.TryNextFixedPage(
                queryStart, sourceRowsCount, FixedPageSize, pagesElapsed,
                _options.MaxPagesPerLoad, _logger, method, out int nextStart);
            if (!hasNext)
            {
                MoexLogMessages.FixedPagePaginationStopped(
                    _logger, method, "last_page_incomplete", pagesElapsed, totalRows,
                    state.PageAcceptedRows, FixedPageSize);
                break;
            }
            queryStart = nextStart;
            query["start"] = queryStart.ToString(CultureInfo.InvariantCulture);
        }
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

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
