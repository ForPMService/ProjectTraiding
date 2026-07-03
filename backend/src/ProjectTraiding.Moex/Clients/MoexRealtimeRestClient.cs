using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
// _Old: using ProjectTraiding.Moex.Contracts.Dto.MarketStatistics;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
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
    /// <summary>
    /// HTTP-клиент для текущих REST-данных MOEX через платный APIM-доступ.
    /// Использует Moex:ApimBaseUrl и Authorization: Bearer {Moex:AlgKey}.
    /// Даже вызовы с query-параметрами iss.only/iss.meta идут через APIM/Bearer,
    /// а не через публичный ISS base URL без ключа.
    /// Подключается к тому же HTTP pipeline: rate limiter, logging handler, Polly resilience.
    /// 
    /// Endpoint-ы:
    ///   stock orderbook:  /engines/stock/markets/shares/boards/TQBR/securities/{ticker}/orderbook.json
    ///   stock trades:     /engines/stock/markets/shares/boards/TQBR/securities/{ticker}/trades.json
    ///   stock candles:    /engines/stock/markets/shares/boards/TQBR/securities/{ticker}/candles.json
    ///   futures orderbook: /engines/futures/markets/forts/boards/RFUD/securities/{ticker}/orderbook.json
    ///   futures trades:    /engines/futures/markets/forts/boards/RFUD/securities/{ticker}/trades.json
    ///   futures candles:   /engines/futures/markets/forts/boards/RFUD/securities/{ticker}/candles.json
    /// 
    /// Парсинг: ParsingRealtimeRestUtf8 (orderbook, trades), ParsingAlgUtf8 (candles).
    /// </summary>
    public class MoexRealtimeRestClient
    {


        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexRealtimeRestClient> _logger;
        private readonly MoexRawCaptureWriter _captureWriter;

        public MoexRealtimeRestClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexRealtimeRestClient> logger,
            MoexRawCaptureWriter captureWriter)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
            _captureWriter = captureWriter;
        }

        // ═══════════════════════════════════════════════════════════
        // Orderbook
        // ═══════════════════════════════════════════════════════════

        public async Task<RealtimeOrderbookParseResult> GetOrderbookStockAsync(
            string ticker,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "orderbook");
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Stock);

            string endpoint = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/orderbook.json";
            return await GetOrderbookAsync(endpoint, cancellationToken);
        }

        public async Task<RealtimeOrderbookParseResult> GetOrderbookFuturesAsync(
            string ticker,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "orderbook");
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Futures);

            string endpoint = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/orderbook.json";
            return await GetOrderbookAsync(endpoint, cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════
        // Trades
        // ═══════════════════════════════════════════════════════════

        public async Task<RealtimeTradesParseResult<RealtimeTradesStockDTO>> GetTradesStockAsync(
            string ticker,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "trades");
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Stock);

            string endpoint = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/trades.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        cancellationToken);
            try
            {
                var result = ParsingRealtimeRestUtf8.ParseTradesStock(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Rows.Count, Stopwatch.GetElapsedTime(startTimestamp));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "trades"));
                MoexMetrics.RowsTotal.Add(
                    result.Rows.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "trades"));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        public async Task<RealtimeTradesParseResult<RealtimeTradesFuturesDTO>> GetTradesFuturesAsync(
            string ticker,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "trades");
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Futures);

            string endpoint = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/trades.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        cancellationToken);
            try
            {
                var result = ParsingRealtimeRestUtf8.ParseTradesFutures(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Rows.Count, Stopwatch.GetElapsedTime(startTimestamp));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "trades"));
                MoexMetrics.RowsTotal.Add(
                    result.Rows.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "trades"));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Candles Today
        // ═══════════════════════════════════════════════════════════

        public async Task<List<CandlesDTO>> GetCandlesTodayStockAsync(
            string ticker,
            DateOnly tradeDate,
            int interval = 1,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.Candles);
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Stock);

            string endpoint = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/candles.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["interval"] = interval.ToString(),
                ["from"] = tradeDate.ToString("yyyy-MM-dd"),
                ["till"] = tradeDate.ToString("yyyy-MM-dd"),
            };
            return await GetCandlesTodayAsync(endpoint, queryParams, cancellationToken);
        }

        public async Task<List<CandlesDTO>> GetCandlesTodayFuturesAsync(
            string ticker,
            DateOnly tradeDate,
            int interval = 1,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.Candles);
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Futures);

            string endpoint = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/candles.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["interval"] = interval.ToString(),
                ["from"] = tradeDate.ToString("yyyy-MM-dd"),
                ["till"] = tradeDate.ToString("yyyy-MM-dd"),
            };
            return await GetCandlesTodayAsync(endpoint, queryParams, cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════
        // MarketStatistics (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        /* _Old: заменён карточками инструментов, парсер ParsingMarketStatisticsUtf8 перенесён
        public async Task<MarketStatisticsStockSecuritiesDTO?> GetMarketStatisticsStockSecuritiesAsync(
           string ticker,
           string? runId = null,
           CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.MarketStats);
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Stock);

            string endpoint = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["iss.only"] = "securities",
                ["iss.meta"] = "off",
            };
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    MarketStatisticsStockSecuritiesDTO? result = ParsingMarketStatisticsUtf8.ParseStockSecurities(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result != null ? 1 : 0, Stopwatch.GetElapsedTime(startTimestamp));
                    MoexMetrics.PagesTotal.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.MarketStats));
                    MoexMetrics.RowsTotal.Add(
                        result != null ? 1 : 0,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.MarketStats));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Realtime,
                        RawCaptureDataTypes.MarketStats,
                        RawCaptureMarkets.Stock,
                        ticker,
                        effectiveRunId,
                        rentedArr.Memory,
                        cancellationToken);
                    return result;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                    if (_captureWriter.IsEnabled)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.SchemaMismatch,
                            RawCaptureClients.Realtime,
                            RawCaptureDataTypes.MarketStats,
                            RawCaptureMarkets.Stock,
                            ticker,
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
                        RawCaptureClients.Realtime,
                        RawCaptureDataTypes.MarketStats,
                        RawCaptureMarkets.Stock,
                        ticker,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        effectiveRunId,
                        RawCaptureKeyBuilder.ResponseFileName());
                    await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                }
                throw;
            }
        }
        */

        /* _Old: заменён GetFuturesInstrumentCards, парсер ParsingMarketStatisticsUtf8 перенесён
        public async Task<MarketStatisticsFuturesSecuritiesDTO?> GetMarketStatisticsFuturesSecuritiesAsync(
            string ticker,
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.MarketStats);
            activity?.SetTag(MoexTelemetryAttributes.Market, RawCaptureMarkets.Futures);

            string endpoint = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["iss.only"] = "securities",
                ["iss.meta"] = "off",
            };
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    MarketStatisticsFuturesSecuritiesDTO? result = ParsingMarketStatisticsUtf8.ParseFuturesSecurities(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result != null ? 1 : 0, Stopwatch.GetElapsedTime(startTimestamp));
                    MoexMetrics.PagesTotal.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.MarketStats));
                    MoexMetrics.RowsTotal.Add(
                        result != null ? 1 : 0,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.MarketStats));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Realtime,
                        RawCaptureDataTypes.MarketStats,
                        RawCaptureMarkets.Futures,
                        ticker,
                        effectiveRunId,
                        rentedArr.Memory,
                        cancellationToken);
                    return result;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                    if (_captureWriter.IsEnabled)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.SchemaMismatch,
                            RawCaptureClients.Realtime,
                            RawCaptureDataTypes.MarketStats,
                            RawCaptureMarkets.Futures,
                            ticker,
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
                        RawCaptureClients.Realtime,
                        RawCaptureDataTypes.MarketStats,
                        RawCaptureMarkets.Futures,
                        ticker,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        effectiveRunId,
                        RawCaptureKeyBuilder.ResponseFileName());
                    await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                }
                throw;
            }
        }
        */

        // ═══════════════════════════════════════════════════════════
        // Raw section (без capture — диагностический метод)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Идёт через общий SendRequestAsync (EnsureSuccessOrThrow, typed errors, timeout).
        /// Raw = «не парсим JSON», а не «обходим ошибки и lifecycle ответа».
        /// Для debug endpoints и диагностики source contract.
        /// </summary>
        public async Task<string> GetRawSectionAsync(
            string method,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            using var response = await SendRequestAsync(method, queryParams, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════
        // Общие приватные методы
        // ═══════════════════════════════════════════════════════════

        private async Task<RealtimeOrderbookParseResult> GetOrderbookAsync(
            string endpoint,
            CancellationToken cancellationToken)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, queryParams: null, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        cancellationToken);
            try
            {
                var result = ParsingRealtimeRestUtf8.ParseOrderbook(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Rows.Count, Stopwatch.GetElapsedTime(startTimestamp));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "orderbook"));
                MoexMetrics.RowsTotal.Add(
                    result.Rows.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, "orderbook"));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        private async Task<List<CandlesDTO>> GetCandlesTodayAsync(
            string endpoint,
            Dictionary<string, string>? queryParams,
            CancellationToken cancellationToken)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        _options.BodyReadTimeout,
                        cancellationToken);
            try
            {
                var result = ParsingAlgUtf8.ParseAlgCandles(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                MoexMetrics.PagesTotal.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.Candles));
                MoexMetrics.RowsTotal.Add(
                    result.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, RawCaptureDataTypes.Candles));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        private async Task<HttpResponseMessage> SendRequestAsync(
    string method,
    Dictionary<string, string>? queryParams = null,
    CancellationToken cancellationToken = default)
        {
            string requestUrl = _options.ApimBaseUrl + method;           // Realtime REST transport: APIM base URL, не публичный ISS.
            queryParams ??= new Dictionary<string, string>();
            if (queryParams.Count > 0)
            {
                QueryString queryString = QueryString.Create(queryParams);
                requestUrl += queryString.ToString();
            }
            EnsureApiKeyConfigured();                                    // ← проверка ключа
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.AlgKey}");  // ← Bearer
            try
            {
                var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await HttpClientHelpers.EnsureSuccessOrThrowAsync(response, method, cancellationToken);
                return response;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX request timeout for {method}", method, "http_client", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx,
                    MoexLogSources.RealtimeRest, method, timeoutEx.ErrorCategory,
                    null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutRejectedException ex)
            {
                var timeoutEx = new MoexTimeoutException(
                    $"MOEX attempt timeout for {method}", method, "polly_attempt", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx,
                    MoexLogSources.RealtimeRest, method, timeoutEx.ErrorCategory,
                    null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutException ex)
            {
                var timeoutEx = new MoexTimeoutException(
                    $"MOEX body read timeout for {method}", method, "body_read",
                    _options.BodyReadTimeout, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.RealtimeRest, method,
                    timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (MoexHttpException ex)
            {
                MoexLogMessages.RequestFailed(_logger, ex,
                    MoexLogSources.RealtimeRest, method, ex.ErrorCategory,
                    (HttpStatusCode?)ex.StatusCode, null, ex.Message);
                throw;
            }
        }

        private void EnsureApiKeyConfigured()
        {
            if (string.IsNullOrWhiteSpace(_options.AlgKey))
            {
                throw new InvalidOperationException(
                    "MOEX ALGOPACK API key is not configured. " +
                    "Set MoexAlg:Key via user-secrets or environment variable.");
            }
        }

    }
}
