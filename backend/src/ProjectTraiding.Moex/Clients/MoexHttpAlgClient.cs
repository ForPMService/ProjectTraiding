using ProjectTraiding.Moex.Clients.Errors;
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
                // Источник тайм-аута берётся из самого исключения: при статусе 408
                // EnsureSuccessOrThrow бросает MoexTimeoutException с заполненным
                // TimeoutSource, и терять его в журнале незачем. У прочих отказов
                // приведение даёт пустое значение — прежнее поведение сохраняется.
                MoexLogMessages.RequestFailed(_logger, ex, MoexLogSources.Algopack, method, ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode, (ex as MoexTimeoutException)?.TimeoutSource, ex.Message);
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
