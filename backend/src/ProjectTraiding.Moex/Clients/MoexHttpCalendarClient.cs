using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpCalendarClient
    {
        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexHttpCalendarClient> _logger;

        public MoexHttpCalendarClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpCalendarClient> logger)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        

        public async Task<List<CalendarOffDaysMarketDTO>> GetStockOffDays(
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Calendar);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Stock);

            const string endpoint = "/calendars/stock.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    _options.BodyReadTimeout,
                    cancellationToken);
            try
            {
                List<CalendarOffDaysMarketDTO> result = ParsingCalendarUtf8.ParseOffDaysMarket(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Calendar),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays));
                MoexMetrics.RowsTotal.Add(
                    result.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Calendar),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        public async Task<List<CalendarOffDaysMarketDTO>> GetFuturesOffDays(
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Calendar);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Futures);

            const string endpoint = "/calendars/futures.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    _options.BodyReadTimeout,
                    cancellationToken);
            try
            {
                List<CalendarOffDaysMarketDTO> result = ParsingCalendarUtf8.ParseOffDaysMarket(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Calendar),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays));
                MoexMetrics.RowsTotal.Add(
                    result.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Calendar),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        

        // ── Инфраструктура ──────────────────────────────────────

        private async Task<HttpResponseMessage> SendRequestAsync(
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
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Calendar, method,
                    timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX request timeout for {method}", method, "http_client", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Calendar, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutRejectedException ex)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX attempt timeout for {method}", method, "polly_attempt", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Calendar, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (MoexHttpException ex)
            {
                MoexLogMessages.RequestFailed(_logger, ex, MoexLogSources.Calendar, method, ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode, null, ex.Message);
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
