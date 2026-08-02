using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Diagnostics;
using System.Globalization;
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

        public MoexRealtimeRestClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexRealtimeRestClient> logger)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════
        // Orderbook
        // ═══════════════════════════════════════════════════════════

        public async Task<RealtimeOrderbookParseResult> GetOrderbookStockAsync(
            string ticker,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["iss.meta"] = "off",
                ["iss.only"] = "orderbook,dataversion",
                ["orderbook.columns"] =
                    ColumnAndNumbersForParsing.RealtimeOrderbookSchema.BuildColumnsParam(),
                ["dataversion.columns"] =
                    ColumnAndNumbersForParsing.RealtimeDataVersionSchema.BuildColumnsParam(),
            };

            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "orderbook");
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Stock);

            string endpoint = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/orderbook.json";
            return await GetOrderbookAsync(endpoint, queryParams, cancellationToken);
        }

        public async Task<RealtimeOrderbookParseResult> GetOrderbookFuturesAsync(
            string ticker,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["iss.meta"] = "off",
                ["iss.only"] = "orderbook,dataversion",
                ["orderbook.columns"] =
                    ColumnAndNumbersForParsing.RealtimeOrderbookSchema.BuildColumnsParam(),
                ["dataversion.columns"] =
                    ColumnAndNumbersForParsing.RealtimeDataVersionSchema.BuildColumnsParam(),
            };

            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "orderbook");
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Futures);

            string endpoint = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/orderbook.json";
            return await GetOrderbookAsync(endpoint, queryParams, cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════
        // Trades
        // ═══════════════════════════════════════════════════════════

        public async Task<RealtimeTradesParseResult<RealtimeTradesStockDTO>> GetTradesStockAsync(
            string ticker,
            long? afterTradeNo = null,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            if (afterTradeNo is not null)
            {
                queryParams["tradeno"] = afterTradeNo.Value.ToString(CultureInfo.InvariantCulture);
                queryParams["next_trade"] = "1";
            }

            queryParams.TryAdd("iss.meta", "off");
            queryParams.TryAdd("iss.only", "trades,dataversion,trades_yields");
            queryParams.TryAdd(
                "trades.columns",
                ColumnAndNumbersForParsing.RealtimeTradesStockSchema.BuildColumnsParam());
            queryParams.TryAdd(
                "dataversion.columns",
                ColumnAndNumbersForParsing.RealtimeDataVersionSchema.BuildColumnsParam());

            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "trades");
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Stock);

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
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        public async Task<RealtimeTradesParseResult<RealtimeTradesFuturesDTO>> GetTradesFuturesAsync(
            string ticker,
            long? afterTradeNo = null,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            if (afterTradeNo is not null)
            {
                queryParams["tradeno"] = afterTradeNo.Value.ToString(CultureInfo.InvariantCulture);
                queryParams["next_trade"] = "1";
            }

            queryParams.TryAdd("iss.meta", "off");
            queryParams.TryAdd("iss.only", "trades,dataversion,trades_yields");
            queryParams.TryAdd(
                "trades.columns",
                ColumnAndNumbersForParsing.RealtimeTradesFuturesSchema.BuildColumnsParam());
            queryParams.TryAdd(
                "dataversion.columns",
                ColumnAndNumbersForParsing.RealtimeDataVersionSchema.BuildColumnsParam());

            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, "trades");
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Futures);

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
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Candles Today
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Свечи торгового дня в окне [from, till]. Окно задаётся МОСКОВСКИМ временем: биржа
        /// отдаёт и принимает московское, часовой пояс машины к делу не относится.
        ///
        /// Догружает все страницы окна. Одно-запросного варианта нет намеренно: у свечей нет
        /// курсора, запрос без смещения всегда отдаёт начало окна, и метод без догрузки после
        /// пятисотой свечи возвращал бы одно и то же вечно — именно это и происходило до правки.
        ///
        /// Опасности всплеска у свечей нет: за минуту рождается ровно одна минутная свеча,
        /// поэтому окно опроса в одну-две минуты укладывается в одну страницу и цикл делает
        /// ровно один запрос.
        /// </summary>
        public async Task<List<CandlesDTO>> GetCandlesTodayStockAsync(
            string ticker,
            DateTime from,
            DateTime till,
            int interval = 1,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Stock);

            string endpoint = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/candles.json";
            return await GetCandlesWindowPagedAsync(endpoint, from, till, interval, cancellationToken);
        }

        /// <summary>
        /// Свечи торгового дня в окне [from, till]. Окно задаётся МОСКОВСКИМ временем: биржа
        /// отдаёт и принимает московское, часовой пояс машины к делу не относится.
        ///
        /// Догружает все страницы окна. Одно-запросного варианта нет намеренно: у свечей нет
        /// курсора, запрос без смещения всегда отдаёт начало окна, и метод без догрузки после
        /// пятисотой свечи возвращал бы одно и то же вечно — именно это и происходило до правки.
        ///
        /// Опасности всплеска у свечей нет: за минуту рождается ровно одна минутная свеча,
        /// поэтому окно опроса в одну-две минуты укладывается в одну страницу и цикл делает
        /// ровно один запрос.
        /// </summary>
        public async Task<List<CandlesDTO>> GetCandlesTodayFuturesAsync(
            string ticker,
            DateTime from,
            DateTime till,
            int interval = 1,
            CancellationToken cancellationToken = default)
        {
            using Activity? activity = MoexTelemetry.ActivitySource.StartActivity("moex.load");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Futures);

            string endpoint = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/candles.json";
            return await GetCandlesWindowPagedAsync(endpoint, from, till, interval, cancellationToken);
        }



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
            Dictionary<string, string> queryParams,
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
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        private const int CandlesPageLimit = 500;

        /// <summary>
        /// Догрузка страниц свечей внутри окна. Признак последней страницы — строк меньше
        /// контрактного размера страницы CandlesPageLimit. Правило то же, что в
        /// MoexHttpAlgClient.GetCandles.
        /// </summary>
        private async Task<List<CandlesDTO>> GetCandlesWindowPagedAsync(
            string endpoint,
            DateTime from,
            DateTime till,
            int interval,
            CancellationToken cancellationToken)
        {
            if (till < from)
            {
                throw new ArgumentException(
                    $"Окно свечей пусто: till={till:yyyy-MM-dd HH:mm:ss} " +
                    $"раньше from={from:yyyy-MM-dd HH:mm:ss}.",
                    nameof(till));
            }

            List<CandlesDTO> rows = new List<CandlesDTO>();
            int start = 0;

            for (int pageNumber = 1; pageNumber <= _options.MaxPagesPerLoad; pageNumber++)
            {
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["iss.meta"] = "off",
                    ["iss.only"] = "candles",
                    ["candles.columns"] =
                        ColumnAndNumbersForParsing.AlgCandlesSchema.BuildColumnsParam(),
                    ["interval"] = interval.ToString(CultureInfo.InvariantCulture),
                    ["from"] = from.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["till"] = till.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                };

                if (start > 0)
                {
                    queryParams["start"] = start.ToString(CultureInfo.InvariantCulture);
                }

                List<CandlesDTO> page =
                    await GetCandlesTodayAsync(endpoint, queryParams, cancellationToken);

                rows.AddRange(page);

                if (page.Count != CandlesPageLimit)
                {
                    return rows;
                }

                if (pageNumber == _options.MaxPagesPerLoad)
                {
                    throw new InvalidOperationException(
                        $"Догрузка свечей достигла защитного предела " +
                        $"Moex:MaxPagesPerLoad={_options.MaxPagesPerLoad} на полной странице.");
                }

                start += CandlesPageLimit;
            }

            throw new InvalidOperationException("Недостижимое состояние пагинации свечей.");
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
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles));
                MoexMetrics.RowsTotal.Add(
                    result.Count,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, MoexLogSources.RealtimeRest),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, MoexDataKinds.Candles));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
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
