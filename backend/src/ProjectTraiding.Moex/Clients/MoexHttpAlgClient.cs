using ProjectTraiding.Moex.Clients.Errors;
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
using System.Text;

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
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using RentedBuffer rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                _options.BodyReadTimeout,
                method,
                cancellationToken);

            return Encoding.UTF8.GetString(rentedArr.Span);
        }

        // ═══════════════════════════════════════════════════════════
        // GetCandles — fixed-page пагинация
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<CandlesDTO>> GetCandles(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? telemetryMarket = null,
            LoadStopOutcome? stopOutcome = null,
            MoexOperationTags? operationTags = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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

                List<CandlesDTO> candlesList;
                using (Activity? pageActivity =
                       MoexTelemetry.ActivitySource.StartActivity("moex.history.fetch"))
                {
                pageActivity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Algopack);
                pageActivity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles);
                if (!string.IsNullOrWhiteSpace(telemetryMarket))
                    pageActivity?.SetTag(MoexTelemetryAttributes.Market, telemetryMarket);

                try
                {
                    HttpResponseMessage response =
                        await SendRequestAsync(method, queryParams, cancellationToken);

                    using (response)
                    {
                        int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                        using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                            await response.Content.ReadAsStreamAsync(cancellationToken),
                            contentLength,
                            _options.BodyReadTimeout,
                            method,
                            cancellationToken);
                        try
                        {
                            candlesList = ParsingAlgUtf8.ParseAlgCandles(rentedArr.Span);
                        }
                        catch (MoexSchemaMismatchException ex)
                        {
                            MoexLogMessages.ParseFailed(_logger, ex, method, MoexErrorTypes.SchemaMismatch, ex.Message);
                            throw;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    pageActivity?.SetStatus(ActivityStatusCode.Ok);
                    if (operationTags is MoexOperationTags cancelTags)
                    {
                        MoexMetrics.RecordOperationCancelled(
                            in cancelTags, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    pageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    if (operationTags is MoexOperationTags errorTags)
                    {
                        MoexMetrics.RecordOperationError(
                            in errorTags, ex, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
                    }

                    throw;
                }

                pagesElapsed++;
                totalRows += candlesList.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, candlesList.Count, Stopwatch.GetElapsedTime(pageStart));

                // Метрики получения данных считаются только для производственного вызова:
                // диагностика набора меток не передаёт, и её обращения в производственный счёт не попадают.
                if (operationTags is MoexOperationTags receivedTags)
                {
                    MoexMetrics.PagesReceived.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, receivedTags.Source),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, receivedTags.DataKind),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, receivedTags.Market));

                    MoexMetrics.RowsReceived.Add(
                        candlesList.Count,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, receivedTags.Source),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, receivedTags.DataKind),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, receivedTags.Market),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Flow, receivedTags.Flow));
                }

                // Операция страницы закончена: дальше управление уходит потребителю.
                if (operationTags is MoexOperationTags successTags)
                {
                    MoexMetrics.RecordOperationSuccess(
                        in successTags, Stopwatch.GetElapsedTime(pageStart).TotalSeconds);
                }

                pageActivity?.SetStatus(ActivityStatusCode.Ok);
                }

                // Отрезок страницы закрыт до передачи страницы потребителю.
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
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.Algopack,
                MoexOperations.ReferenceInstrumentsFetch,
                MoexDataKinds.Instruments,
                MoexMarkets.Futures,
                MoexFlows.History);

            long operationStart = Stopwatch.GetTimestamp();
            try
            {
                const string endpoint =
                    "/engines/futures/markets/forts/boards/RFUD/securities.json?iss.meta=off";
 
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    _options.BodyReadTimeout,
                    endpoint,
                    cancellationToken);
                List<FuturesInstrumentCardDTO> result = ParsingInstrumentCardUtf8.ParseFuturesCards(rentedArr.Span);
                MoexMetrics.RecordOperationSuccess(
                    in operationTags, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                return result;
            }
            catch (OperationCanceledException)
            {
                MoexMetrics.RecordOperationCancelled(
                    in operationTags, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                throw;
            }
            catch (Exception ex)
            {
                MoexMetrics.RecordOperationError(
                    in operationTags, ex, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                throw;
            }
        }
        // ═══════════════════════════════════════════════════════════
        // Инфраструктура
        // ═══════════════════════════════════════════════════════════

        internal async Task<HttpResponseMessage> SendRequestAsync(string method, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default)
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
                HttpClientHelpers.EnsureSuccessOrThrow(response, method);
                return response;
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
