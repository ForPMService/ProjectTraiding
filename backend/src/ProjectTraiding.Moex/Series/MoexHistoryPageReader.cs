using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing.Errors;

namespace ProjectTraiding.Moex.Series;

public sealed class MoexHistoryPageReader
{
    private readonly MoexHttpAlgClient _client;
    private readonly MoexSeriesParser _parser;
    private readonly MoexOptions _options;
    private readonly ILogger<MoexHistoryPageReader> _logger;

    /// <summary>Итог получения одной страницы: строки, курсор ответа и затраченное время.</summary>
    private readonly record struct PageFetchResult(
        SeriesParsedPage Rows,
        PaginationCursorDTO Cursor,
        TimeSpan Elapsed);

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

        int pagesElapsed = 0;
        int totalRows = 0;
        int skippedTotal = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult page = await FetchPageAsync(
                spec, method, query, secId, operationTags, cancellationToken);
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
                skippedTotal = AccountSkippedRows(parsed, method, operationTags, skippedTotal);

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

            skippedTotal = AccountSkippedRows(parsed, method, operationTags, skippedTotal);

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

        const int FixedPageSize = 500;
        int queryStart = 0;
        int pagesElapsed = 0;
        int totalRows = 0;
        int skippedTotal = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult page = await FetchPageAsync(
                spec, method, query, secId, operationTags, cancellationToken);
            SeriesParsedPage parsed = page.Rows;

            skippedTotal = AccountSkippedRows(parsed, method, operationTags, skippedTotal);

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

        int dayIndex = 0;
        int totalRows = 0;
        int skippedTotal = 0;
        DateTime fromDate = from.ToDateTime(TimeOnly.MinValue);
        DateTime tillDate = till.ToDateTime(TimeOnly.MinValue);
        for (DateTime date = fromDate; date <= tillDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            query["from"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            query["till"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            PageFetchResult fetched = await FetchPageAsync(
                spec, method, query, secId, operationTags, cancellationToken);
            SeriesParsedPage parsed = fetched.Rows;

            if (parsed.SourceRowsCount > 0)
            {
                skippedTotal = AccountSkippedRows(parsed, method, operationTags, skippedTotal);
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
    /// Учёт отвергнутых строк страницы: предел, событие журнала, счётчик с метками.
    /// Возвращает накопленное число отвергнутых строк.
    /// </summary>
    private int AccountSkippedRows(
        SeriesParsedPage parsed,
        string method,
        MoexOperationTags operationTags,
        int skippedTotal)
    {
        skippedTotal += parsed.SkippedRows;
        if (_options.MaxSkippedRowsPerLoad >= 0
            && skippedTotal > _options.MaxSkippedRowsPerLoad)
        {
            // Испорченная строка — редкость, испорченная страница — признак сломанного
            // контракта. Порог отделяет одно от другого: до него загрузка продолжается,
            // после — задание отменяется целиком.
            throw new InvalidOperationException(
                $"Разбор отверг {skippedTotal} строк, предел {_options.MaxSkippedRowsPerLoad}: " +
                "источник вернул данные, не отвечающие декларации.");
        }

        if (parsed.SkippedRows == 0)
            return skippedTotal;

        MoexLogMessages.RowsSkipped(_logger, method, parsed.SkippedRows);
        MoexMetrics.RowsSkipped.Add(
            parsed.SkippedRows,
            new KeyValuePair<string, object?>(
                MoexTelemetryAttributes.Source, operationTags.Source),
            new KeyValuePair<string, object?>(
                MoexTelemetryAttributes.DataKind, operationTags.DataKind),
            new KeyValuePair<string, object?>(
                MoexTelemetryAttributes.Market, operationTags.Market));

        return skippedTotal;
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
    private async Task<PageFetchResult> FetchPageAsync(
        MoexSeriesSpec spec,
        string method,
        Dictionary<string, string> query,
        string secId,
        MoexOperationTags operationTags,
        CancellationToken cancellationToken)
    {
        long pageStart = Stopwatch.GetTimestamp();

        SeriesParsedPage parsed;
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
                    method, query, cancellationToken);
                using RentedBuffer body = await RentedBuffer.RentFromResponseAsync(
                    response, _options.BodyReadTimeout, method, cancellationToken);

                try
                {
                    parsed = _parser.Parse(body.Span, spec, secId, out cursor);
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
            MoexMetrics.RowsReceived.Add(parsed.Rows.Count, rowsTags);

            MoexMetrics.RecordOperationSuccess(
                in operationTags,
                Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            pageActivity?.SetStatus(ActivityStatusCode.Ok);
        }

        return new PageFetchResult(parsed, cursor, elapsed);
    }
}
