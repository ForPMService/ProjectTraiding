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
        List<(object?[] Row, DateTime Time)> Rows,
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

    public async IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> ReadPages(
        MoexSeriesSpec spec,
        string secId,
        string boardId,
        DateOnly from,
        DateOnly till,
        LoadStopOutcome stopOutcome,
        MoexOperationTags operationTags,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (spec.Pagination == PaginationKind.Cursor)
        {
            await foreach (List<(object?[] Row, DateTime Time)> page in ReadCursorPages(
                               spec,
                               secId,
                               from,
                               till,
                               stopOutcome,
                               operationTags,
                               cancellationToken))
            {
                yield return page;
            }

            yield break;
        }

        if (spec.Pagination == PaginationKind.FixedPage)
        {
            await foreach (List<(object?[] Row, DateTime Time)> page in ReadFixedPages(
                               spec,
                               secId,
                               boardId,
                               from,
                               till,
                               stopOutcome,
                               operationTags,
                               cancellationToken))
            {
                yield return page;
            }

            yield break;
        }

        await foreach (List<(object?[] Row, DateTime Time)> page in ReadDaySplitPages(
                           spec,
                           secId,
                           from,
                           till,
                           stopOutcome,
                           operationTags,
                           cancellationToken))
        {
            yield return page;
        }
    }

    private async IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> ReadCursorPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        LoadStopOutcome stopOutcome,
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
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult page = await FetchPageAsync(
                spec, method, query, secId, operationTags, cancellationToken);
            List<(object?[] Row, DateTime Time)> rows = page.Rows;
            PaginationCursorDTO cursor = page.Cursor;

            pagesElapsed++;
            totalRows += rows.Count;
            MoexLogMessages.PageReceived(
                _logger, method, pagesElapsed, rows.Count, page.Elapsed);

            // Решение об остановке принимается до обрезки: последняя страница отдаётся
            // целиком, иначе её хвостовая группа была бы отброшена безвозвратно.
            PaginationStep step = MoexCursorPagination.Next(
                cursor, pagesElapsed, _options.MaxPagesPerLoad);
            if (step.IsStop)
            {
                yield return rows;

                MoexLogMessages.PaginationStopped(
                    _logger, method, step.StopReason!, pagesElapsed, totalRows);
                stopOutcome.Complete(
                    step.StopReason!,
                    isPartial: step.StopReason == "safety_cap_hit");
                break;
            }

            int nextStart = step.NextStart;

            // Граница страницы не должна рассекать группу строк одного момента: порядок
            // внутри группы источник не гарантирует, и рассечённая группа теряет строку.
            // Хвост отбрасывается и дочитывается следующей страницей целиком.
            if (spec.PreserveCursorTimeGroup && rows.Count > 0)
            {
                DateTime lastTime = rows[^1].Time;
                int tailStart = rows.Count - 1;
                while (tailStart > 0 && rows[tailStart - 1].Time == lastTime)
                    tailStart--;

                if (tailStart == 0)
                    throw new InvalidOperationException(
                        $"Страница {method} целиком занята одной временной группой "
                        + $"({rows.Count} строк на момент {lastTime:yyyy-MM-dd HH:mm:ss}), "
                        + "продолжение чтения не гарантирует полноты.");

                nextStart = cursor.Index!.Value + tailStart;
                rows = rows.GetRange(0, tailStart);
            }

            yield return rows;

            query["start"] = nextStart.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> ReadFixedPages(
        MoexSeriesSpec spec,
        string secId,
        string boardId,
        DateOnly from,
        DateOnly till,
        LoadStopOutcome stopOutcome,
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
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageFetchResult page = await FetchPageAsync(
                spec, method, query, secId, operationTags, cancellationToken);
            List<(object?[] Row, DateTime Time)> rows = page.Rows;

            pagesElapsed++;
            totalRows += rows.Count;
            MoexLogMessages.PageReceived(
                _logger, method, pagesElapsed, rows.Count, page.Elapsed);

            yield return rows;

            if (rows.Count >= FixedPageSize)
            {
                queryStart += FixedPageSize;
                query["start"] = queryStart.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                MoexLogMessages.FixedPagePaginationStopped(
                    _logger, method, "last_page_incomplete",
                    pagesElapsed, totalRows, rows.Count, FixedPageSize);
                stopOutcome.Complete("range_exhausted", isPartial: false);
                break;
            }
        }
    }

    private async IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> ReadDaySplitPages(
        MoexSeriesSpec spec,
        string secId,
        DateOnly from,
        DateOnly till,
        LoadStopOutcome stopOutcome,
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
        DateTime fromDate = from.ToDateTime(TimeOnly.MinValue);
        DateTime tillDate = till.ToDateTime(TimeOnly.MinValue);
        for (DateTime date = fromDate; date <= tillDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            query["from"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            query["till"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            PageFetchResult fetched = await FetchPageAsync(
                spec, method, query, secId, operationTags, cancellationToken);
            List<(object?[] Row, DateTime Time)> page = fetched.Rows;

            dayIndex++;
            totalRows += page.Count;
            MoexLogMessages.DaySplitPageReceived(
                _logger,
                method,
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                page.Count,
                fetched.Elapsed);

            if (page.Count > 0)
                yield return page;
        }

        MoexLogMessages.DaySplitCompleted(
            _logger, method, fromText, tillText, dayIndex, totalRows);
        stopOutcome.Complete("range_exhausted", isPartial: false);
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

        List<(object?[] Row, DateTime Time)> rows;
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
                    rows = _parser.Parse(body.Span, spec, secId, out cursor);
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
            MoexMetrics.RowsReceived.Add(rows.Count, rowsTags);

            MoexMetrics.RecordOperationSuccess(
                in operationTags,
                Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
            pageActivity?.SetStatus(ActivityStatusCode.Ok);
        }

        return new PageFetchResult(rows, cursor, elapsed);
    }
}
