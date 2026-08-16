using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Realtime.Series;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

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
    /// Парсинг всех блоков приёма выполняет MoexRealtimeParser по декларациям серий.
    /// </summary>
    public class MoexRealtimeRestClient
    {
        private const string StockPrefix =
            "/engines/stock/markets/shares/boards/TQBR/securities/";
        private const string FuturesPrefix =
            "/engines/futures/markets/forts/boards/RFUD/securities/";

        // Перечень колонок блока версии данных постоянен: собирать его на каждом опросе
        // значило бы создавать массив имён, раскодировать каждое из двоичного представления
        // и склеивать результат — на каждый инструмент, на каждом обороте.
        private static readonly string DataVersionColumnsParam =
            ColumnAndNumbersForParsing.RealtimeDataVersionSchema.BuildColumnsParam();


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
        // Единый тракт по декларациям приёма
        // ═══════════════════════════════════════════════════════════

        public async Task<RealtimeParsedPage> GetTradesAsync(
            string market,
            string ticker,
            long? afterTradeNo = null,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            MoexRealtimeSpec spec = MoexRealtimeRegistry.TradesFor(market);
            queryParams ??= new Dictionary<string, string>();
            if (afterTradeNo is not null)
            {
                queryParams["tradeno"] =
                    afterTradeNo.Value.ToString(CultureInfo.InvariantCulture);
                queryParams["next_trade"] = "1";
            }

            queryParams.TryAdd("iss.meta", "off");
            queryParams.TryAdd("iss.only", "trades,dataversion");
            queryParams.TryAdd("trades.columns", spec.ColumnsParam);
            queryParams.TryAdd("dataversion.columns", DataVersionColumnsParam);

            string endpoint = BuildEndpoint(market, ticker, "/trades.json");
            long startTimestamp = Stopwatch.GetTimestamp();
            using HttpResponseMessage response =
                await SendRequestAsync(endpoint, queryParams, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using RentedBuffer rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                _options.BodyReadTimeout,
                endpoint,
                cancellationToken);
            try
            {
                RealtimeParsedPage result =
                    MoexRealtimeParser.ParsePage(rentedArr.Span, spec, ticker);
                MoexLogMessages.SinglePageReceived(
                    _logger,
                    endpoint,
                    result.Rows.Count,
                    Stopwatch.GetElapsedTime(startTimestamp));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(
                    _logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        public async Task<RealtimeParsedPage> GetOrderbookAsync(
            string market,
            string ticker,
            CancellationToken cancellationToken = default)
        {
            MoexRealtimeSpec spec = MoexRealtimeRegistry.Orderbook;
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["iss.meta"] = "off",
                ["iss.only"] = "orderbook,dataversion",
                ["orderbook.columns"] = spec.ColumnsParam,
                ["dataversion.columns"] = DataVersionColumnsParam,
            };

            string endpoint = BuildEndpoint(market, ticker, "/orderbook.json");
            long startTimestamp = Stopwatch.GetTimestamp();
            using HttpResponseMessage response =
                await SendRequestAsync(endpoint, queryParams, cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using RentedBuffer rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                _options.BodyReadTimeout,
                endpoint,
                cancellationToken);
            try
            {
                RealtimeParsedPage result =
                    MoexRealtimeParser.ParsePage(rentedArr.Span, spec, ticker);
                MoexLogMessages.SinglePageReceived(
                    _logger,
                    endpoint,
                    result.Rows.Count,
                    Stopwatch.GetElapsedTime(startTimestamp));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(
                    _logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Свечи торгового дня в московском окне [from, till]. Все страницы окна
        /// догружаются здесь, потому что у свечей нет курсора следующей записи.
        /// </summary>
        public async Task<List<(object?[] Row, DateTime? Begin)>> GetCandlesTodayAsync(
            string market,
            string ticker,
            DateTime from,
            DateTime till,
            int interval = 1,
            CancellationToken cancellationToken = default)
        {
            MoexRealtimeSpec spec = MoexRealtimeRegistry.CandlesFor(interval);
            string endpoint = BuildEndpoint(market, ticker, "/candles.json");
            if (till < from)
            {
                throw new ArgumentException(
                    $"Окно свечей пусто: till={till:yyyy-MM-dd HH:mm:ss} " +
                    $"раньше from={from:yyyy-MM-dd HH:mm:ss}.",
                    nameof(till));
            }

            List<(object?[] Row, DateTime? Begin)> rows = [];
            int start = 0;

            for (int pageNumber = 1; pageNumber <= _options.MaxPagesPerLoad; pageNumber++)
            {
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["iss.meta"] = "off",
                    ["iss.only"] = "candles",
                    ["candles.columns"] = spec.ColumnsParam,
                    ["interval"] = interval.ToString(CultureInfo.InvariantCulture),
                    ["from"] = from.ToString(
                        "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["till"] = till.ToString(
                        "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                };

                if (start > 0)
                    queryParams["start"] = start.ToString(CultureInfo.InvariantCulture);

                long startTimestamp = Stopwatch.GetTimestamp();
                using HttpResponseMessage response =
                    await SendRequestAsync(endpoint, queryParams, cancellationToken);
                int contentLength =
                    (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using RentedBuffer rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    _options.BodyReadTimeout,
                    endpoint,
                    cancellationToken);

                List<(object?[] Row, DateTime? Begin)> page;
                try
                {
                    page = MoexRealtimeParser.ParseCandles(rentedArr.Span, spec, ticker);
                    MoexLogMessages.SinglePageReceived(
                        _logger,
                        endpoint,
                        page.Count,
                        Stopwatch.GetElapsedTime(startTimestamp));
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(
                        _logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                    throw;
                }

                rows.AddRange(page);

                if (page.Count != CandlesPageLimit)
                    return rows;

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
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                _options.BodyReadTimeout,
                method,
                cancellationToken);
            return Encoding.UTF8.GetString(rentedArr.Span);
        }

        // ═══════════════════════════════════════════════════════════
        // Общие приватные методы
        // ═══════════════════════════════════════════════════════════

        // Адрес приёма отличается от исторического: доска постоянна для рынка, окна и курсора
        // в пути нет. Поэтому адрес строит клиент, а не декларация.
        //
        // Ветки перечислены явно, без «иначе — фьючерсы». Молчаливый откат к одному из рынков
        // превратил бы опечатку в рынке в обращение не по тому адресу; сообщение то же, что
        // у служб при открытии состояния.
        private static string BuildEndpoint(string market, string ticker, string tail)
        {
            if (market == MoexMarkets.Stock)
                return string.Concat(StockPrefix, ticker, tail);
            if (market == MoexMarkets.Futures)
                return string.Concat(FuturesPrefix, ticker, tail);

            throw new InvalidOperationException(
                $"Неизвестный рынок инструмента приёмника: '{market}'.");
        }

        private const int CandlesPageLimit = 500;

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
                HttpClientHelpers.EnsureSuccessOrThrow(response, method);
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
            catch (MoexHttpException ex)
            {
                // Источник тайм-аута берётся из самого исключения: при статусе 408
                // EnsureSuccessOrThrow бросает MoexTimeoutException с заполненным
                // TimeoutSource, и терять его в журнале незачем. У прочих отказов
                // приведение даёт пустое значение — прежнее поведение сохраняется.
                MoexLogMessages.RequestFailed(_logger, ex,
                    MoexLogSources.RealtimeRest, method, ex.ErrorCategory,
                    (HttpStatusCode?)ex.StatusCode,
                    (ex as MoexTimeoutException)?.TimeoutSource, ex.Message);
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
