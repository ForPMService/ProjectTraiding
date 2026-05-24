using History_DataMoex.Clients.Errors;
using History_DataMoex.Contracts.Dto.Iss;
using History_DataMoex.Infrastructure.Buffers;
using History_DataMoex.Options;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using System.Diagnostics;
using System.Net;


namespace History_DataMoex.Clients
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
        /// Diagnostic only. Does not use typed error handling (no MoexHttpException hierarchy,
        /// no EnsureSuccessOrThrow, no structured logging).
        /// Do NOT use for production raw capture. Phase 8 uses SendRequestAsync-based path
        /// in GetInfoTradedStockAssetsRaw / GetInfoTradedFuturesAssetsRaw (added in Phase 8-C).
        /// Used by DebugEndpoints only. Reliability fix: separate cleanup task after Phase 8-D.
        /// Lock §12.
        /// </summary>
        public async Task<string> GetRaw(
            string method,
            CancellationToken cancellationToken = default)
        {
            string requestUrl = _options.IssBaseUrl + method;
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<List<StockSecurityDTO>> GetInfoTradedStockAssets(
            string method,
            CancellationToken cancellationToken = default)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(method, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                cancellationToken);
            try
            {
                List<StockSecurityDTO> result = ParsingIssUtf8.ParseIssSecurityStock(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, method, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                throw;
            }
        }

        public async Task<List<FuturesSecurityDTO>> GetInfoTradedFuturesAssets(
            string method,
            CancellationToken cancellationToken = default)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(method, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                cancellationToken);
            try
            {
                List<FuturesSecurityDTO> result = ParsingIssUtf8.ParseIssSecurityFutures(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, method, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                throw;
            }
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
                var timeoutEx = new MoexTimeoutException($"MOEX request timeout for {method}", method, "http_client", _options.RequestTimeout, ex);
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
