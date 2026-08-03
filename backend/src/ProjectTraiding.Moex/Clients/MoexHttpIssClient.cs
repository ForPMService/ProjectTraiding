using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Diagnostics;
using System.Net;


namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpIssClient
    {
        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexHttpIssClient> _logger;

        public MoexHttpIssClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpIssClient> logger)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Карточки всех акций TQBR — securities + marketdata одним запросом.
        ///
        /// Источник: /engines/stock/markets/shares/boards/tqbr/securities.json?iss.meta=off
        /// Режим: ISS (публичный, без ключа).
        /// Парсер: ParsingInstrumentCardUtf8.ParseStockCards (два прохода: securities + marketdata).
        /// </summary>
        public async Task<List<StockInstrumentCardDTO>> GetStockInstrumentCards(
            CancellationToken cancellationToken = default)
        {
            const string endpoint =
                "/engines/stock/markets/shares/boards/tqbr/securities.json?iss.meta=off";
 
            using var response = await SendRequestAsync(endpoint, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                _options.BodyReadTimeout,
                endpoint,
                cancellationToken);
            return ParsingInstrumentCardUtf8.ParseStockCards(rentedArr.Span);
        }
        private async Task<HttpResponseMessage> SendRequestAsync(string method, CancellationToken cancellationToken)
        {
            string requestUrl = _options.IssBaseUrl + method;
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            try
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                HttpClientHelpers.EnsureSuccessOrThrow(response, method);
                return response;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX request timeout for {method}", method, "http_client", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Iss, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutRejectedException ex)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX attempt timeout for {method}", method, "polly_attempt", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Iss, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (MoexHttpException ex)
            {
                MoexLogMessages.RequestFailed(_logger, ex, MoexLogSources.Iss, method, ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode, (ex as MoexTimeoutException)?.TimeoutSource, ex.Message);
                throw;
            }
        }

    }
}
