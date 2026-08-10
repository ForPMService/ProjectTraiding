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
        string taskSecId,
        string requestKey,
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
                               taskSecId,
                               requestKey,
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
                           taskSecId,
                           requestKey,
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
        string taskSecId,
        string requestKey,
        DateOnly from,
        DateOnly till,
        LoadStopOutcome stopOutcome,
        MoexOperationTags operationTags,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string method = string.Format(
            CultureInfo.InvariantCulture, spec.MethodTemplate, requestKey);
        Dictionary<string, string> query = new()
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["till"] = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["iss.meta"] = "off",
            ["iss.only"] = "data,data.cursor",
            ["data.columns"] = spec.BuildColumnsParam(),
        };

        int pagesElapsed = 0;
        int totalRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long pageStart = Stopwatch.GetTimestamp();

            List<(object?[] Row, DateTime Time)> rows;
            PaginationCursorDTO cursor;
            using (Activity? pageActivity =
                   MoexTelemetry.ActivitySource.StartActivity("moex.history.fetch"))
            {
                pageActivity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
                pageActivity?.SetTag(MoexTelemetryAttributes.DataKind, spec.TelemetryDataKind);
                pageActivity?.SetTag(MoexTelemetryAttributes.Market, spec.Market);

                try
                {
                    using HttpResponseMessage response = await _client.SendRequestAsync(
                        method, query, cancellationToken);
                    int contentLength =
                        (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using RentedBuffer body = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        method,
                        cancellationToken);

                    try
                    {
                        rows = _parser.Parse(body.Span, spec, taskSecId, out cursor);
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

                pagesElapsed++;
                totalRows += rows.Count;
                MoexLogMessages.PageReceived(
                    _logger,
                    method,
                    pagesElapsed,
                    rows.Count,
                    Stopwatch.GetElapsedTime(pageStart));

                MoexMetrics.PagesReceived.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Source, operationTags.Source),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.DataKind, operationTags.DataKind),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Market, operationTags.Market));

                MoexMetrics.RowsReceived.Add(
                    rows.Count,
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Source, operationTags.Source),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.DataKind, operationTags.DataKind),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Market, operationTags.Market),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Flow, operationTags.Flow));

                MoexMetrics.RecordOperationSuccess(
                    in operationTags,
                    Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
                pageActivity?.SetStatus(ActivityStatusCode.Ok);
            }

            yield return rows;

            PaginationStep step = MoexCursorPagination.Next(
                cursor, pagesElapsed, _options.MaxPagesPerLoad);
            if (step.IsStop)
            {
                MoexLogMessages.PaginationStopped(
                    _logger, method, step.StopReason!, pagesElapsed, totalRows);
                stopOutcome.Complete(
                    step.StopReason!,
                    isPartial: step.StopReason == "safety_cap_hit");
                break;
            }

            query["start"] = step.NextStart.ToString();
        }
    }

    private async IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> ReadDaySplitPages(
        MoexSeriesSpec spec,
        string taskSecId,
        string requestKey,
        DateOnly from,
        DateOnly till,
        LoadStopOutcome stopOutcome,
        MoexOperationTags operationTags,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string method = string.Format(
            CultureInfo.InvariantCulture, spec.MethodTemplate, requestKey);
        string fromText = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string tillText = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Dictionary<string, string> query = new()
        {
            ["from"] = fromText,
            ["till"] = tillText,
            ["iss.meta"] = "off",
        };

        query.Remove("start");
        query.Remove("offset");

        int dayIndex = 0;
        int totalRows = 0;
        DateTime fromDate = from.ToDateTime(TimeOnly.MinValue);
        DateTime tillDate = till.ToDateTime(TimeOnly.MinValue);
        for (DateTime date = fromDate; date <= tillDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            query["from"] = date.ToString("yyyy-MM-dd");
            query["till"] = date.ToString("yyyy-MM-dd");
            long pageStart = Stopwatch.GetTimestamp();

            List<(object?[] Row, DateTime Time)> page;
            using (Activity? pageActivity =
                   MoexTelemetry.ActivitySource.StartActivity("moex.history.fetch"))
            {
                pageActivity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
                pageActivity?.SetTag(MoexTelemetryAttributes.DataKind, spec.TelemetryDataKind);
                pageActivity?.SetTag(MoexTelemetryAttributes.Market, spec.Market);

                try
                {
                    using HttpResponseMessage response = await _client.SendRequestAsync(
                        method, query, cancellationToken);
                    int contentLength =
                        (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using RentedBuffer body = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        method,
                        cancellationToken);

                    try
                    {
                        page = _parser.Parse(body.Span, spec, taskSecId, out _);
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

                dayIndex++;
                totalRows += page.Count;
                MoexLogMessages.DaySplitPageReceived(
                    _logger,
                    method,
                    date.ToString("yyyy-MM-dd"),
                    page.Count,
                    Stopwatch.GetElapsedTime(pageStart));

                MoexMetrics.PagesReceived.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Source, operationTags.Source),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.DataKind, operationTags.DataKind),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Market, operationTags.Market));

                MoexMetrics.RowsReceived.Add(
                    page.Count,
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Source, operationTags.Source),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.DataKind, operationTags.DataKind),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Market, operationTags.Market),
                    new KeyValuePair<string, object?>(
                        MoexTelemetryAttributes.Flow, operationTags.Flow));

                MoexMetrics.RecordOperationSuccess(
                    in operationTags,
                    Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
                pageActivity?.SetStatus(ActivityStatusCode.Ok);
            }

            if (page.Count > 0)
                yield return page;
        }

        MoexLogMessages.DaySplitCompleted(
            _logger, method, fromText, tillText, dayIndex, totalRows);
        stopOutcome.Complete("range_exhausted", isPartial: false);
    }
}
