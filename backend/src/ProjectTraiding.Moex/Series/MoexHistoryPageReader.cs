using System.Diagnostics;
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
    private readonly MoexSeriesParser _parser;
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

    public MoexHistoryPageReader(
        MoexHttpAlgClient client,
        MoexSeriesParser parser,
        IOptions<MoexOptions> options,
        ILogger<MoexHistoryPageReader> logger)
    {
        _client = client;
        _parser = parser;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Выбор стратегии постраничного чтения. Метод обычный, а не асинхронный итератор:
    /// выбирать нечего, кроме одной из трёх последовательностей, а обёртка добавляла
    /// свою машину состояний и лишний шаг продвижения на каждую страницу.
    ///
    /// Ветвление выполняется при вызове, а не при первом обращении к последовательности.
    /// Проверок доводов и иных побочных действий в нём нет, поэтому наблюдаемое поведение
    /// не меняется.
    /// </summary>
    public IAsyncEnumerable<SeriesParsedPage> ReadPages(
        MoexSeriesSpec spec,
        string secId,
        string boardId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        CancellationToken cancellationToken)
    {
        if (spec.Pagination == PaginationKind.Cursor)
        {
            return ReadCursorPages(
                spec, secId, from, till, operationTags, cancellationToken);
        }

        if (spec.Pagination == PaginationKind.FixedPage)
        {
            return ReadFixedPages(
                spec, secId, boardId, from, till, operationTags, cancellationToken);
        }

        return ReadDaySplitPages(
            spec, secId, from, till, operationTags, cancellationToken);
    }

    public IAsyncEnumerable<TradeStatsParsedPage<TradeStatsStockRow>> ReadTradeStatsStockPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        CancellationToken cancellationToken)
    {
        PageParser<TradeStatsParsedPage<TradeStatsStockRow>> parsePage =
            (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
                TradeStatsHistoryParser.ParseStock(body.Span, spec, secId, out cursor);
        return ReadTradeStatsCursorPages(
            spec, secId, from, till, operationTags, parsePage, cancellationToken);
    }

    public IAsyncEnumerable<TradeStatsParsedPage<TradeStatsFuturesRow>> ReadTradeStatsFuturesPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        CancellationToken cancellationToken)
    {
        PageParser<TradeStatsParsedPage<TradeStatsFuturesRow>> parsePage =
            (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
                TradeStatsHistoryParser.ParseFutures(body.Span, spec, secId, out cursor);
        return ReadTradeStatsCursorPages(
            spec, secId, from, till, operationTags, parsePage, cancellationToken);
    }

    private async IAsyncEnumerable<TradeStatsParsedPage<TRow>> ReadTradeStatsCursorPages<TRow>(
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
            PageFetchResult<TradeStatsParsedPage<TRow>> page = await FetchPageAsync(
                spec, method, query, secId, operationTags,
                parsePage, static parsed => parsed.Rows.Count, cancellationToken);
            TradeStatsParsedPage<TRow> parsed = page.Rows;
            PaginationCursorDTO cursor = page.Cursor;

            pagesElapsed++;
            totalRows += parsed.Rows.Count;
            MoexLogMessages.PageReceived(
                _logger, method, pagesElapsed, parsed.Rows.Count, page.Elapsed);

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

    private async IAsyncEnumerable<SeriesParsedPage> ReadCursorPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
        PageParser<SeriesParsedPage> parsePage = CreateSeriesPageParser(spec, secId);

        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult<SeriesParsedPage> page = await FetchPageAsync(
                spec, method, query, secId, operationTags,
                parsePage, static parsed => parsed.Rows.Count, cancellationToken);
            SeriesParsedPage parsed = page.Rows;
            PaginationCursorDTO cursor = page.Cursor;

            pagesElapsed++;
            totalRows += parsed.Rows.Count;
            MoexLogMessages.PageReceived(
                _logger, method, pagesElapsed, parsed.Rows.Count, page.Elapsed);

            // Решение об остановке принимается до обрезки: последняя страница отдаётся
            // целиком, иначе её хвостовая группа была бы отброшена безвозвратно.
            if (cursor.Index is null || cursor.PageSize is null || cursor.Total is null)
                throw new InvalidOperationException(
                    "Источник не вернул курсор целиком (INDEX, TOTAL, PAGESIZE): " +
                    "полнота диапазона не доказана.");

            int cursorIndex = cursor.Index.Value;
            int cursorTotal = cursor.Total.Value;
            int cursorPageSize = cursor.PageSize.Value;

            if (cursorIndex < 0 || cursorTotal < 0)
                throw new InvalidOperationException(
                    $"Курсор источника отрицателен: INDEX={cursorIndex}, TOTAL={cursorTotal}.");

            if (cursorPageSize <= 0)
                throw new InvalidOperationException(
                    $"Размер страницы источника не положителен: PAGESIZE={cursorPageSize}.");

            if (cursorIndex > cursorTotal)
                throw new InvalidOperationException(
                    $"Курсор указывает за пределы набора: INDEX={cursorIndex}, TOTAL={cursorTotal}.");

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
                throw new InvalidOperationException(
                    $"Достигнут защитный предел Moex:MaxPagesPerLoad={_options.MaxPagesPerLoad} " +
                    "при неисчерпанном диапазоне.");

            int nextStart = (int)nextCursor;

            // Граница страницы не должна рассекать группу строк одного момента: порядок
            // внутри группы источник не гарантирует, и рассечённая группа теряет строку.
            // Хвост отбрасывается и дочитывается следующей страницей целиком.
            if (spec.PreserveCursorTimeGroup && parsed.SourceRowsCount > 0)
            {
                // Границу группы ищем по позициям ответа источника, а не по списку принятых
                // строк. Отвергнутая строка тоже занимает позицию и несёт свой момент,
                // поэтому участвует в поиске наравне с принятой: группа может начинаться
                // именно с неё, и перезапуск после неё рассёк бы группу — ровно то,
                // ради чего признак и введён.
                (int sourceTailStart, int acceptedTailStart, DateTime? groupTime) =
                    FindTailGroupStart(parsed);

                if (sourceTailStart == 0)
                    throw new InvalidOperationException(
                        $"Страница {method} целиком занята одной временной группой "
                        + $"({parsed.SourceRowsCount} строк источника на момент "
                        + $"{groupTime:yyyy-MM-dd HH:mm:ss}), "
                        + "продолжение чтения не гарантирует полноты.");

                nextStart = cursorIndex + sourceTailStart;
                parsed = TrimToSourceIndex(parsed, acceptedTailStart, sourceTailStart);
            }

            AccountSkippedRows(parsed, method, secId, operationTags, query);

            yield return parsed;

            query["start"] = nextStart.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async IAsyncEnumerable<SeriesParsedPage> ReadFixedPages(
        MoexSeriesSpec spec,
        string secId,
        string boardId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Постраничное чтение с фиксированным размером. Форма запроса и правило
        // остановки перенесены дословно из прежнего GetCandles: страница 500 строк,
        // сдвиг start, остановка на неполной странице. Параметр data.columns свечной
        // запрос не шлёт — форма запроса к бирже неизменна. Регистр доски в адресе
        // разный: у акций строчными, у фьючерсов прописными — форма, проверенная
        // диагностикой.
        if (spec.CandleInterval is not int candleInterval)
            throw new InvalidOperationException(
                $"Декларация {spec.DataKind}/{spec.Market} с постраничной пагинацией без интервала.");

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

        PageParser<SeriesParsedPage> parsePage = CreateSeriesPageParser(spec, secId);
        int queryStart = 0;
        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult<SeriesParsedPage> page = await FetchPageAsync(
                spec, method, query, secId, operationTags,
                parsePage, static parsed => parsed.Rows.Count, cancellationToken);
            SeriesParsedPage parsed = page.Rows;

            AccountSkippedRows(parsed, method, secId, operationTags, query);

            pagesElapsed++;
            totalRows += parsed.Rows.Count;
            MoexLogMessages.PageReceived(
                _logger, method, pagesElapsed, parsed.Rows.Count, page.Elapsed);

            yield return parsed;

            if (parsed.SourceRowsCount >= FixedPageSize)
            {
                if (pagesElapsed >= _options.MaxPagesPerLoad)
                    throw new InvalidOperationException(
                        $"Достигнут защитный предел Moex:MaxPagesPerLoad={_options.MaxPagesPerLoad} " +
                        "при неисчерпанном диапазоне.");

                queryStart += FixedPageSize;
                query["start"] = queryStart.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                MoexLogMessages.FixedPagePaginationStopped(
                    _logger, method, "last_page_incomplete",
                    pagesElapsed, totalRows, parsed.Rows.Count, FixedPageSize);
                break;
            }
        }
    }

    /// <summary>
    /// Typed fixed-page route shared by historical candle intervals and markets.
    /// Its request and advancement rules match ReadFixedPages; page fetching, body
    /// management, error handling, and telemetry still go through FetchPageAsync.
    /// </summary>
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

    private async IAsyncEnumerable<SeriesParsedPage> ReadDaySplitPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        MoexOperationTags operationTags,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string method = string.Format(
            CultureInfo.InvariantCulture, spec.MethodTemplate, secId);
        string fromText = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string tillText = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Dictionary<string, string> query = new()
        {
            ["from"] = fromText,
            ["till"] = tillText,
            ["iss.meta"] = "off",
        };
        PageParser<SeriesParsedPage> parsePage = CreateSeriesPageParser(spec, secId);

        int dayIndex = 0;
        int totalRows = 0;
        DateTime fromDate = from.ToDateTime(TimeOnly.MinValue);
        DateTime tillDate = till.ToDateTime(TimeOnly.MinValue);
        for (DateTime date = fromDate; date <= tillDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            query["from"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            query["till"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            PageFetchResult<SeriesParsedPage> fetched;
            Exception? fetchFailure = null;
            try
            {
                fetched = await FetchPageAsync(
                    spec, method, query, secId, operationTags,
                    parsePage, static parsed => parsed.Rows.Count, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableReadFailure(ex))
            {
                fetchFailure = ex;
                fetched = default;
            }

            if (fetchFailure is not null)
            {
                yield return new SeriesParsedPage(
                    [], 0, null, new SeriesReadFailure(
                        DateOnly.FromDateTime(date), DateOnly.FromDateTime(date), fetchFailure.Message));
                dayIndex++;
                continue;
            }
            SeriesParsedPage parsed = fetched.Rows;

            if (parsed.SourceRowsCount > 0)
            {
                AccountSkippedRows(parsed, method, secId, operationTags, query);
            }

            dayIndex++;
            totalRows += parsed.Rows.Count;
            MoexLogMessages.DaySplitPageReceived(
                _logger,
                method,
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                parsed.Rows.Count,
                fetched.Elapsed);

            // Условие по числу исходных строк, а не принятых: страница, у которой отвергнуты
            // все строки, всё равно прочитана и обязана дойти до писателя — иначе её строки
            // не попадут ни в покрытый объём, ни в счёт пропусков.
            if (parsed.SourceRowsCount > 0)
                yield return parsed;
        }

        MoexLogMessages.DaySplitCompleted(
            _logger, method, fromText, tillText, dayIndex, totalRows);
    }

    /// <summary>
    /// Учёт отвергнутых строк страницы: события журнала и счётчик с метками.
    /// </summary>
    private void AccountSkippedRows(
        SeriesParsedPage parsed,
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

    /// <summary>
    /// Начало последней временной группы: позиция в ответе источника, число принятых
    /// строк до неё и момент самой группы. Обход идёт по позициям ответа от конца
    /// к началу, принятые и отвергнутые строки участвуют в нём наравне.
    ///
    /// Опорный момент берётся у первой встреченной строки, момент которой известен.
    /// Строка с неизвестным моментом считается принадлежащей группе: перезапуск раньше
    /// начала группы лишь перечитает несколько строк, перезапуск позже рассечёт группу
    /// и потеряет строку, потому что порядок строк внутри группы источник не гарантирует.
    /// Из двух неточностей выбрана безвредная.
    ///
    /// Нулевая позиция означает, что границы нет: либо вся страница занята одной группой,
    /// либо ни у одной её строки момент прочитать не удалось.
    /// </summary>
    private static (int SourceTailStart, int AcceptedTailStart, DateTime? GroupTime)
        FindTailGroupStart(SeriesParsedPage page)
    {
        int acceptedIndex = page.Rows.Count - 1;
        int skippedIndex = (page.SkippedSourceRows?.Count ?? 0) - 1;
        DateTime? groupTime = null;

        for (int sourceIndex = page.SourceRowsCount - 1; sourceIndex >= 0; sourceIndex--)
        {
            bool accepted = true;
            DateTime? time;
            if (skippedIndex >= 0
                && page.SkippedSourceRows![skippedIndex].SourceIndex == sourceIndex)
            {
                accepted = false;
                time = page.SkippedSourceRows[skippedIndex].Time;
                skippedIndex--;
            }
            else
            {
                time = page.Rows[acceptedIndex].Time;
            }

            if (groupTime is null)
            {
                groupTime = time;
            }
            else if (time is not null && time != groupTime)
            {
                return (sourceIndex + 1, acceptedIndex + 1, groupTime);
            }

            if (accepted)
                acceptedIndex--;
        }

        return (0, 0, groupTime);
    }

    /// <summary>
    /// Обрезает страницу по границе хвостовой группы. Отвергнутые строки хвоста в счёт
    /// этой страницы не идут: следующая страница начнётся с той же позиции источника
    /// и отвергнет их заново — иначе они были бы посчитаны дважды.
    /// </summary>
    private static SeriesParsedPage TrimToSourceIndex(
        SeriesParsedPage page, int acceptedCount, int sourceCount)
    {
        List<SkippedSourceRow>? skipped = null;
        if (page.SkippedSourceRows is not null)
        {
            for (int i = 0; i < page.SkippedSourceRows.Count; i++)
            {
                if (page.SkippedSourceRows[i].SourceIndex >= sourceCount)
                    break;

                skipped ??= new List<SkippedSourceRow>();
                skipped.Add(page.SkippedSourceRows[i]);
            }
        }

        return new SeriesParsedPage(
            page.Rows.GetRange(0, acceptedCount), sourceCount, skipped);
    }

    /// <summary>
    /// Получает и разбирает одну страницу: спан, обработка отмены и ошибки, метрики страницы.
    /// Стратегиям пагинации остаётся только правило продвижения и запись в журнал —
    /// форма записи у них разная, поэтому она наверху, а не здесь.
    /// </summary>
    private PageParser<SeriesParsedPage> CreateSeriesPageParser(
        MoexSeriesSpec spec,
        string secId)
    {
        return (ReadOnlyMemory<byte> body, out PaginationCursorDTO cursor) =>
            _parser.Parse(body.Span, spec, secId, out cursor);
    }

    private async Task<PageFetchResult<TPage>> FetchPageAsync<TPage>(
        MoexSeriesSpec spec,
        string method,
        Dictionary<string, string> query,
        string secId,
        MoexOperationTags operationTags,
        PageParser<TPage> parsePage,
        Func<TPage, int> getAcceptedRowCount,
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
            MoexMetrics.RowsReceived.Add(getAcceptedRowCount(parsed), rowsTags);

            MoexMetrics.RecordOperationSuccess(
                in operationTags,
                Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            pageActivity?.SetStatus(ActivityStatusCode.Ok);
        }

        return new PageFetchResult<TPage>(parsed, cursor, elapsed);
    }
}
