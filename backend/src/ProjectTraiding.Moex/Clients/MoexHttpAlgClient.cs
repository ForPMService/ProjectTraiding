using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using ProjectTraiding.Moex.Infrastructure.RawCapture;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpAlgClient
    {
        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexHttpAlgClient> _logger;
        private readonly MoexRawCaptureWriter _captureWriter;

        public MoexHttpAlgClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpAlgClient> logger,
            MoexRawCaptureWriter captureWriter)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
            _captureWriter = captureWriter;
        }

        public async Task<string> GetRaw(
            string method,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            string requestUrl = _options.ApimBaseUrl + method;
            queryParams ??= new Dictionary<string, string>();
            if (queryParams.Count > 0)
            {
                QueryString queryString = QueryString.Create(queryParams);
                requestUrl += queryString.ToString();
            }
            EnsureApiKeyConfigured();
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.AlgKey}");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            // Не бросаем — видим что MOEX ответил
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════
        // GetCandles — fixed-page пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<CandlesDTO>> GetCandles(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? captureMarket = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles);
            if (!string.IsNullOrWhiteSpace(captureMarket))
            {
                activity?.SetTag(MoexTelemetryAttributes.Market, captureMarket);
            }

            queryParams ??= new Dictionary<string, string>();
            queryParams["iss.meta"] = "off";
            queryParams["iss.only"] = "candles";
            int queryStart = 0;

            if (queryParams.TryGetValue("start", out string? start) && int.TryParse(start, out int parseValue))
            {
                queryStart = parseValue;
            }

            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            var accumulator = new RawCaptureAccumulator(
                _captureWriter,
                RawCaptureClients.Alg,
                MoexDataKinds.Candles,
                captureMarket,
                secid,
                effectiveRunId);
            int pagesElapsed = 0;
            int totalRows = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(method, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Alg,
                            MoexDataKinds.Candles,
                            captureMarket, secid,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                List<CandlesDTO> candlesList;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        cancellationToken);
                    try
                    {
                        candlesList = ParsingAlgUtf8.ParseAlgCandles(rentedArr.Span);
                        await accumulator.AppendPageAsync(rentedArr.Memory, pagesElapsed, cancellationToken);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Alg,
                                MoexDataKinds.Candles,
                                captureMarket, secid,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
                }

                pagesElapsed++;
                totalRows += candlesList.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, candlesList.Count, Stopwatch.GetElapsedTime(pageStart));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Algopack),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles));
                MoexMetrics.RowsTotal.Add(
                    candlesList.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Algopack),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles));
                yield return candlesList;
                if (candlesList.Count >= 500)
                {
                    queryStart += 500;
                    queryParams["start"] = queryStart.ToString();
                }
                else
                {
                    MoexLogMessages.FixedPagePaginationStopped(_logger, method, "last_page_incomplete", pagesElapsed, totalRows, candlesList.Count, 500);
                    stopOutcome?.Complete("range_exhausted", isPartial: false);
                    break;
                }
            }

            activity?.SetTag("total_pages", pagesElapsed);
            activity?.SetTag("total_rows", totalRows);

        }

        // ═══════════════════════════════════════════════════════════
        // SuperCandles — cursor-пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<TRow>> GetCursorPages<TKind, TRow>(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TKind : struct, IAlgCursorKind<TRow>
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, TKind.TelemetryDataKind);
            activity?.SetTag(MoexTelemetryAttributes.Market, TKind.TelemetryMarket);

            queryParams ??= new Dictionary<string, string>();
            queryParams["iss.meta"] = "off";
            queryParams["iss.only"] = "data,data.cursor";
            queryParams["data.columns"] = TKind.Schema.BuildColumnsParam();
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            var accumulator = new RawCaptureAccumulator(
                _captureWriter,
                RawCaptureClients.Alg,
                TKind.TelemetryDataKind,
                TKind.TelemetryMarket,
                secid,
                effectiveRunId);
            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(method, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Alg,
                            TKind.TelemetryDataKind,
                            TKind.TelemetryMarket, secid,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                List<TRow> rows;
                PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        cancellationToken);
                    try
                    {
                        rows = TKind.Parse(rentedArr.Span, out cursor);
                        await accumulator.AppendPageAsync(rentedArr.Memory, pagesElapsed, cancellationToken);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Alg,
                                TKind.TelemetryDataKind,
                                TKind.TelemetryMarket, secid,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
                }

                pagesElapsed++;
                totalRows += rows.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, rows.Count, Stopwatch.GetElapsedTime(pageStart));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Algopack),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, TKind.TelemetryDataKind));
                MoexMetrics.RowsTotal.Add(
                    rows.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Algopack),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, TKind.TelemetryDataKind));
                yield return rows;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    stopOutcome?.Complete(step.StopReason!, isPartial: step.StopReason == "safety_cap_hit");
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }

            activity?.SetTag("total_pages", pagesElapsed);
            activity?.SetTag("total_rows", totalRows);

        }

        public IAsyncEnumerable<List<SuperCandlesTradeStats5mDTO>> GetSuperCandlesTradeStats5m(
          string method,
          Dictionary<string, string>? queryParams = null,
          string? runId = null,
          string? secid = null,
          LoadStopOutcome? stopOutcome = null,
          CancellationToken cancellationToken = default)
            => GetCursorPages<TradeStatsStockCursorKind, SuperCandlesTradeStats5mDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        public IAsyncEnumerable<List<SuperCandlesFuturesTradeStats5mDTO>> GetSuperCandlesFuturesTradeStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<TradeStatsFuturesCursorKind, SuperCandlesFuturesTradeStats5mDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        public IAsyncEnumerable<List<SuperCandlesOrderBookStats5mDTO>> GetSuperCandlesOrderBookStats5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<ObStatsStockCursorKind, SuperCandlesOrderBookStats5mDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        public IAsyncEnumerable<List<SuperCandlesFuturesOrderBookStats5mDTO>> GetSuperCandlesFuturesOrderBookStats5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<ObStatsFuturesCursorKind, SuperCandlesFuturesOrderBookStats5mDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        public IAsyncEnumerable<List<SuperCandlesOrderStats5mDTO>> GetSuperCandlesOrderStats5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<OrderStatsStockCursorKind, SuperCandlesOrderStats5mDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        // ═══════════════════════════════════════════════════════════
        // FUTOI — day-split пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<FutoiDTO>> StreamFutoi(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.Futoi);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Futures);

            queryParams ??= new Dictionary<string, string>();
            queryParams["iss.meta"] = "off";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            // FUTOI API не поддерживает пагинацию (start игнорируется, лимит 1000 строк).
            // Разбиваем диапазон дат по одному дню — один день Si ≈ 470 строк, всегда < 1000.

            if (!queryParams.TryGetValue("from", out string? fromStr)
                || !queryParams.TryGetValue("till", out string? tillStr))
            {
                throw new InvalidOperationException(
                    "StreamFutoi requires 'from' and 'till' in queryParams.");
            }

            if (!DateTime.TryParseExact(fromStr, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime fromDate)
                || !DateTime.TryParseExact(tillStr, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime tillDate))
            {
                throw new InvalidOperationException(
                    $"StreamFutoi: invalid date format. from='{fromStr}', till='{tillStr}'. Expected yyyy-MM-dd.");
            }

            // Убрать start/offset если были — MOEX их игнорирует
            queryParams.Remove("start");
            queryParams.Remove("offset");

            var accumulator = new RawCaptureAccumulator(
                _captureWriter,
                RawCaptureClients.Alg,
                MoexDataKinds.Futoi,
                MoexMarkets.Futures,
                secid,
                effectiveRunId);
            int dayIndex = 0;
            int totalRows = 0;

            for (DateTime date = fromDate; date <= tillDate; date = date.AddDays(1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                queryParams["from"] = date.ToString("yyyy-MM-dd");
                queryParams["till"] = date.ToString("yyyy-MM-dd");

                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(method, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Alg,
                            MoexDataKinds.Futoi,
                            MoexMarkets.Futures, secid,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.DateFileName(DateOnly.FromDateTime(date)));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                List<FutoiDTO> page;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        cancellationToken);
                    try
                    {
                        page = ParsingAlgUtf8.ParseFutoi(rentedArr.Span);
                        await accumulator.AppendPageAsync(rentedArr.Memory, dayIndex, cancellationToken);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Alg,
                                MoexDataKinds.Futoi,
                                MoexMarkets.Futures, secid,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.DateFileName(DateOnly.FromDateTime(date)));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
                }

                dayIndex++;
                totalRows += page.Count;
                MoexLogMessages.DaySplitPageReceived(_logger, method, date.ToString("yyyy-MM-dd"), page.Count, Stopwatch.GetElapsedTime(pageStart));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Algopack),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.Futoi));
                MoexMetrics.RowsTotal.Add(
                    page.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Algopack),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.Futoi));

                if (page.Count > 0)
                {
                    yield return page;
                }
            }
            MoexLogMessages.DaySplitCompleted(_logger, method, fromStr, tillStr, dayIndex, totalRows);

            // Дневное разбиение всегда проходит весь диапазон целиком: защитного предела страниц
            // здесь нет, поэтому исход только штатный — исчерпание диапазона, не частичный.
            stopOutcome?.Complete("range_exhausted", isPartial: false);

            activity?.SetTag("total_pages", dayIndex);
            activity?.SetTag("total_rows", totalRows);

        }

        // ═══════════════════════════════════════════════════════════
        // HI2 — cursor-пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public IAsyncEnumerable<List<Hi2AssetDTO>> GetHi2Asset5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<Hi2StockCursorKind, Hi2AssetDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        public IAsyncEnumerable<List<Hi2FuturesDTO>> GetHi2Futures5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<Hi2FuturesCursorKind, Hi2FuturesDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        // ═══════════════════════════════════════════════════════════
        // MegaAlerts — cursor-пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public IAsyncEnumerable<List<MegaAlertsAssetsDTO>> GetMegaAlerts(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<MegaAlertsStockCursorKind, MegaAlertsAssetsDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        public IAsyncEnumerable<List<MegaAlertsFuturesDTO>> GetMegaAlertsFutures(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            LoadStopOutcome? stopOutcome = null,
            CancellationToken cancellationToken = default)
            => GetCursorPages<MegaAlertsFuturesCursorKind, MegaAlertsFuturesDTO>(
                method, queryParams, runId, secid, stopOutcome, cancellationToken);

        // <summary>
        /// Карточки всех фьючерсов RFUD — securities + marketdata одним запросом.
        ///
        /// Источник: /engines/futures/markets/forts/boards/RFUD/securities.json?iss.meta=off
        /// Режим: APIM (платный, Bearer). ISS отдаёт пустые BID/OFFER по фьючерсам.
        /// Парсер: ParsingInstrumentCardUtf8.ParseFuturesCards (два прохода: securities + marketdata).
        /// </summary>
        public async Task<List<FuturesInstrumentCardDTO>> GetFuturesInstrumentCards(
            CancellationToken cancellationToken = default)
        {
            const string endpoint =
                "/engines/futures/markets/forts/boards/RFUD/securities.json?iss.meta=off";
 
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return ParsingInstrumentCardUtf8.ParseFuturesCards(bytes);
        }
        // ═══════════════════════════════════════════════════════════
        // Инфраструктура
        // ═══════════════════════════════════════════════════════════

        private async Task<HttpResponseMessage> SendRequestAsync(string method, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default)
        {
            string baseUrl = _options.ApimBaseUrl;
            string requestUrl = baseUrl + method;
            queryParams ??= new Dictionary<string, string>();
            if (queryParams.Count > 0)
            {
                QueryString queryString = QueryString.Create(queryParams!);
                requestUrl += queryString.ToString();
            }
            EnsureApiKeyConfigured();
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.AlgKey}");
            try
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await HttpClientHelpers.EnsureSuccessOrThrowAsync(response, method, cancellationToken);
                return response;
            }
            catch (TimeoutException ex)
            {
                var timeoutEx = new MoexTimeoutException(
                    $"MOEX body read timeout for {method}", method, "body_read",
                    _options.BodyReadTimeout, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Algopack, method,
                    timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX request timeout for {method}", method, "http_client", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Algopack, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutRejectedException ex)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX attempt timeout for {method}", method, "polly_attempt", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Algopack, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (MoexHttpException ex)
            {
                MoexLogMessages.RequestFailed(_logger, ex, MoexLogSources.Algopack, method, ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode, null, ex.Message);
                throw;
            }
        }

        private void EnsureApiKeyConfigured()
        {
            if (string.IsNullOrWhiteSpace(_options.AlgKey))
            {
                throw new InvalidOperationException(
                    "MOEX ALGOPACK API key is not configured. Set MoexAlg:Key via user-secrets or environment variable.");
            }
        }
    }
}
