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

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpAlgClient
    {
        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexHttpAlgClient> _logger;

        public MoexHttpAlgClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpAlgClient> logger)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Диагностическое получение исходного тела ответа ALGOPACK. Успешность статуса
        /// намеренно не проверяется: сырая точка существует ради того, чтобы увидеть тело
        /// ошибки Московской биржи так же, как тело успешного ответа. Освобождение запроса
        /// и ответа происходит после полного чтения тела — область using закрывается только
        /// при выходе из метода, то есть после завершения чтения.
        /// </summary>
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
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.AlgKey}");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════
        // GetCandles — fixed-page пагинация
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<CandlesDTO>> GetCandles(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? telemetryMarket = null,
            LoadStopOutcome? stopOutcome = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles);
            if (!string.IsNullOrWhiteSpace(telemetryMarket))
            {
                activity?.SetTag(MoexTelemetryAttributes.Market, telemetryMarket);
            }

            queryParams ??= new Dictionary<string, string>();
            queryParams["iss.meta"] = "off";
            queryParams["iss.only"] = "candles";
            int queryStart = 0;

            if (queryParams.TryGetValue("start", out string? start) && int.TryParse(start, out int parseValue))
            {
                queryStart = parseValue;
            }

            int pagesElapsed = 0;
            int totalRows = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response =
                    await SendRequestAsync(method, queryParams, cancellationToken);

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
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
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
        // ALGOPACK — cursor-пагинация
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<TRow>> GetCursorPages<TKind, TRow>(
            string method,
            Dictionary<string, string>? queryParams = null,
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
            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response =
                    await SendRequestAsync(method, queryParams, cancellationToken);

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
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
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

        // ═══════════════════════════════════════════════════════════
        // FUTOI — day-split пагинация
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<FutoiDTO>> StreamFutoi(
            string method,
            Dictionary<string, string>? queryParams = null,
            LoadStopOutcome? stopOutcome = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.Futoi);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Futures);

            queryParams ??= new Dictionary<string, string>();
            queryParams["iss.meta"] = "off";
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

            int dayIndex = 0;
            int totalRows = 0;

            for (DateTime date = fromDate; date <= tillDate; date = date.AddDays(1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                queryParams["from"] = date.ToString("yyyy-MM-dd");
                queryParams["till"] = date.ToString("yyyy-MM-dd");

                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response =
                    await SendRequestAsync(method, queryParams, cancellationToken);

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
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
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
