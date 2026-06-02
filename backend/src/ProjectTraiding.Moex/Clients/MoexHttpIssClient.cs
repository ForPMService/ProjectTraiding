using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.RawCapture;
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
        private readonly MoexRawCaptureWriter _captureWriter;

        public MoexHttpIssClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpIssClient> logger,
            MoexRawCaptureWriter captureWriter)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
            _captureWriter = captureWriter;
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
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Iss);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "securities");
            activity?.SetTag(MoexTelemetryAttributes.Market, "stock");

            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
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
                    MoexMetrics.PagesTotal.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Iss));
                    MoexMetrics.RowsTotal.Add(
                        result.Count,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Iss),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "securities"));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Iss,
                        RawCaptureDataTypes.Securities,
                        RawCaptureMarkets.Stock,
                        null,
                        effectiveRunId,
                        rentedArr.Memory,
                        cancellationToken);
                    return result;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    if (_captureWriter.IsEnabled)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.SchemaMismatch,
                            RawCaptureClients.Iss,
                            RawCaptureDataTypes.Securities,
                            RawCaptureMarkets.Stock,
                            null,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.ResponseFileName());
                        await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                    }
                    throw;
                }
            }
            catch (MoexHttpException ex)
            {
                if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                {
                    string key = RawCaptureKeyBuilder.BuildErrorKey(
                        RawCaptureErrorTypes.HttpError,
                        RawCaptureClients.Iss,
                        RawCaptureDataTypes.Securities,
                        RawCaptureMarkets.Stock,
                        null,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        effectiveRunId,
                        RawCaptureKeyBuilder.ResponseFileName());
                    await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                }
                throw;
            }
        }

        public async Task<List<FuturesSecurityDTO>> GetInfoTradedFuturesAssets(
            string method,
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Iss);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "securities");
            activity?.SetTag(MoexTelemetryAttributes.Market, "futures");

            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
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
                    MoexMetrics.PagesTotal.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Iss));
                    MoexMetrics.RowsTotal.Add(
                        result.Count,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.Iss),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "securities"));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Iss,
                        RawCaptureDataTypes.Securities,
                        RawCaptureMarkets.Futures,
                        null,
                        effectiveRunId,
                        rentedArr.Memory,
                        cancellationToken);
                    return result;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    if (_captureWriter.IsEnabled)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.SchemaMismatch,
                            RawCaptureClients.Iss,
                            RawCaptureDataTypes.Securities,
                            RawCaptureMarkets.Futures,
                            null,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.ResponseFileName());
                        await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                    }
                    throw;
                }
            }
            catch (MoexHttpException ex)
            {
                if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                {
                    string key = RawCaptureKeyBuilder.BuildErrorKey(
                        RawCaptureErrorTypes.HttpError,
                        RawCaptureClients.Iss,
                        RawCaptureDataTypes.Securities,
                        RawCaptureMarkets.Futures,
                        null,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        effectiveRunId,
                        RawCaptureKeyBuilder.ResponseFileName());
                    await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                }
                throw;
            }
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
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return ParsingInstrumentCardUtf8.ParseStockCards(bytes);
        }
        private async Task<HttpResponseMessage> SendRequestAsync(string method, CancellationToken cancellationToken)
        {
            string requestUrl = _options.IssBaseUrl + method;
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            try
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await HttpClientHelpers.EnsureSuccessOrThrowAsync(response, method, cancellationToken);
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
