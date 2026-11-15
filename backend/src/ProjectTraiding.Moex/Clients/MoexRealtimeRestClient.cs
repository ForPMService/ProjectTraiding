using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Realtime.Series;
using ProjectTraiding.Moex.Realtime.Subscriptions;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    /// Парсинг ответов приёма выполняет статический MoexRealtimeParser.
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

        // Набор параметров запроса стакана постоянен и не зависит от вызова: собирать
        // одинаковый словарь на каждый опрос каждого инструмента незачем.
        //
        // Этот словарь не изменяет никто и никогда. Тип оставлен обычным намеренно:
        // замороженный словарь не определяет порядок ключей, а порядок пар задаёт вид
        // адреса, который попадает в события журнала обращений. Поиска по ключу здесь
        // нет — набор только перечисляется при сборке адреса, поэтому менять тип не на что.
        private static readonly Dictionary<string, string> OrderbookQueryParams = new()
        {
            ["iss.meta"] = "off",
            ["iss.only"] = "orderbook,dataversion",
            ["orderbook.columns"] = MoexRealtimeRegistry.OrderbookColumnsParam,
            ["dataversion.columns"] = DataVersionColumnsParam,
        };

        // Набор параметров запроса сделок постоянен, пока не задан номер сделки: он
        // зависит только от рынка через перечень колонок декларации. Готовые словари
        // собираются по одному на рынок и никем не изменяются — их только перечисляют
        // при сборке адреса. Порядок пар тот же, что был у сборки на месте.
        private static readonly Dictionary<string, string> TradesStockQueryParams =
            BuildTradesQueryParams(MoexRealtimeRegistry.TradesStockColumnsParam);

        private static readonly Dictionary<string, string> TradesFuturesQueryParams =
            BuildTradesQueryParams(MoexRealtimeRegistry.TradesFuturesColumnsParam);

        private static Dictionary<string, string> BuildTradesQueryParams(string columnsParam) =>
            new()
            {
                ["iss.meta"] = "off",
                ["iss.only"] = "trades,dataversion",
                ["trades.columns"] = columnsParam,
                ["dataversion.columns"] = DataVersionColumnsParam,
            };

        private readonly MoexOptions _options;
        private readonly ILogger<MoexRealtimeRestClient> _logger;
        private readonly MoexHttpTransport _transport;

        public MoexRealtimeRestClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexRealtimeRestClient> logger)
        {
            _options = options.Value;
            _logger = logger;
            _transport = new MoexHttpTransport(
                httpClient,
                logger,
                _options,
                _options.ApimBaseUrl,
                MoexLogSources.RealtimeRest,
                requiresApiKey: true);
        }

        // ═══════════════════════════════════════════════════════════
        // Типизированные realtime endpoints
        // ═══════════════════════════════════════════════════════════

        public async Task<RealtimeStockTradesPage> GetStockTradesAsync(
            string ticker,
            long? afterTradeNo = null,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            if (queryParams is null && afterTradeNo is null)
            {
                // Ни один ключ не зависит от вызова — берётся готовый словарь рынка.
                // Дополнять его нечем, поэтому ветка дополнения обходится: запись
                // в общий словарь была бы недопустима при нескольких приёмниках.
                queryParams = TradesStockQueryParams;
            }
            else
            {
                queryParams ??= new Dictionary<string, string>(6);
                if (afterTradeNo is not null)
                {
                    queryParams["tradeno"] =
                        afterTradeNo.Value.ToString(CultureInfo.InvariantCulture);
                    queryParams["next_trade"] = "1";
                }

                queryParams.TryAdd("iss.meta", "off");
                queryParams.TryAdd("iss.only", "trades,dataversion");
                queryParams.TryAdd("trades.columns", MoexRealtimeRegistry.TradesStockColumnsParam);
                queryParams.TryAdd("dataversion.columns", DataVersionColumnsParam);
            }

            string endpoint = BuildEndpoint(MoexMarkets.Stock, ticker, "/trades.json");
            using HttpResponseMessage response =
                await _transport.SendAsync(
                    endpoint, queryParams, cancellationToken, MoexFlows.Realtime);
            using RentedBuffer rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                return MoexRealtimeParser.ParseStockTrades(rentedArr.Span, ticker);
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(
                    _logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        public async Task<RealtimeFuturesTradesPage> GetFuturesTradesAsync(
            string ticker,
            long? afterTradeNo = null,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            if (queryParams is null && afterTradeNo is null)
            {
                queryParams = TradesFuturesQueryParams;
            }
            else
            {
                queryParams ??= new Dictionary<string, string>(6);
                if (afterTradeNo is not null)
                {
                    queryParams["tradeno"] =
                        afterTradeNo.Value.ToString(CultureInfo.InvariantCulture);
                    queryParams["next_trade"] = "1";
                }
                queryParams.TryAdd("iss.meta", "off");
                queryParams.TryAdd("iss.only", "trades,dataversion");
                queryParams.TryAdd("trades.columns", MoexRealtimeRegistry.TradesFuturesColumnsParam);
                queryParams.TryAdd("dataversion.columns", DataVersionColumnsParam);
            }

            string endpoint = BuildEndpoint(MoexMarkets.Futures, ticker, "/trades.json");
            using HttpResponseMessage response =
                await _transport.SendAsync(
                    endpoint, queryParams, cancellationToken, MoexFlows.Realtime);
            using RentedBuffer rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                return MoexRealtimeParser.ParseFuturesTrades(rentedArr.Span, ticker);
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(
                    _logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        public async Task<RealtimeOrderbookPage> GetOrderbookAsync(
            string market,
            string ticker,
            CancellationToken cancellationToken = default)
        {
            string endpoint = BuildEndpoint(market, ticker, "/orderbook.json");
            using HttpResponseMessage response =
                await _transport.SendAsync(
                    endpoint, OrderbookQueryParams, cancellationToken, MoexFlows.Realtime);
            using RentedBuffer rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                return MoexRealtimeParser.ParseOrderbook(rentedArr.Span, ticker);
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
        public async IAsyncEnumerable<RealtimeCandlesPageSummary> GetCandlesTodayPagesAsync(
            string market,
            string ticker,
            DateTime from,
            DateTime till,
            int interval,
            ProtobufBatchWriter<CandleRow> writer,
            DateTime now,
            DateTime? lastBegin,
            CandleSubscriptionRegistry candleSubscriptions,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string endpoint = BuildEndpoint(market, ticker, "/candles.json");
            if (till < from)
            {
                throw new ArgumentException(
                    $"Окно свечей пусто: till={till:yyyy-MM-dd HH:mm:ss} " +
                    $"раньше from={from:yyyy-MM-dd HH:mm:ss}.",
                    nameof(till));
            }

            int start = 0;

            // Окно и перечень колонок на весь вызов одни: собирать словарь и форматировать
            // границы окна на каждой странице незачем. Порядок ключей сохранён — от него
            // зависит вид адреса, попадающего в события журнала обращений. Смещение
            // добавляется со второй страницы, как и прежде.
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["iss.meta"] = "off",
                ["iss.only"] = "candles",
                ["candles.columns"] = MoexRealtimeRegistry.CandlesColumnsParam,
                ["interval"] = interval.ToString(CultureInfo.InvariantCulture),
                ["from"] = from.ToString(
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["till"] = till.ToString(
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };

            for (int pageNumber = 1; pageNumber <= _options.MaxPagesPerLoad; pageNumber++)
            {
                if (start > 0)
                    queryParams["start"] = start.ToString(CultureInfo.InvariantCulture);

                List<CandleRow>? acceptedCandles = candleSubscriptions.HasSubscribers
                    ? new List<CandleRow>()
                    : null;

                RealtimeCandlesPageSummary page = await FetchCandlesPageAsync(
                    endpoint,
                    queryParams,
                    ticker,
                    writer,
                    now,
                    lastBegin,
                    acceptedCandles,
                    cancellationToken);

                for (int i = 0; i < page.RowFailures.Count; i++)
                {
                    RealtimeRowFailure failure = page.RowFailures[i];
                    page.RowFailures[i] = failure with { Position = start + failure.Position };
                }

                yield return page;

                if (page.SourceRowsCount != CandlesPageLimit)
                    yield break;

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

        private async Task<RealtimeCandlesPageSummary> FetchCandlesPageAsync(
            string endpoint,
            Dictionary<string, string> queryParams,
            string ticker,
            ProtobufBatchWriter<CandleRow> writer,
            DateTime now,
            DateTime? lastBegin,
            List<CandleRow>? acceptedCandles,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _transport.SendAsync(
                endpoint, queryParams, cancellationToken, MoexFlows.Realtime);
            using RentedBuffer rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                return MoexRealtimeParser.ParseCandles(
                    rentedArr.Span, ticker, writer, now, lastBegin, acceptedCandles);
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(
                    _logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Raw section (без capture — диагностический метод)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Идёт через общий транспорт: проверка статуса, типизированные ошибки, тайм-аут.
        /// Raw = «не парсим JSON», а не «обходим ошибки и lifecycle ответа».
        /// Для debug endpoints и диагностики source contract.
        /// </summary>
        public async Task<string> GetRawSectionAsync(
            string method,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            using var response = await _transport.SendAsync(
                method, queryParams, cancellationToken, MoexFlows.History);
            using var rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, method, cancellationToken);
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

    }
}
