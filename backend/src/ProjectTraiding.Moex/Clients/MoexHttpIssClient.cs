using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using System.Diagnostics;
using System.Globalization;
using System.Text;


namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpIssClient
    {
        private readonly MoexOptions _options;
        private readonly MoexHttpTransport _transport;

        public MoexHttpIssClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpIssClient> logger)
        {
            _options = options.Value;
            _transport = new MoexHttpTransport(
                httpClient,
                logger,
                _options,
                _options.IssBaseUrl,
                MoexLogSources.Iss,
                requiresApiKey: false);
        }

        /// <summary>
        /// Диагностическое получение исходного тела ответа ISS. Успешность статуса
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
            using HttpResponseMessage response =
                await _transport.SendWithoutStatusCheckAsync(method, queryParams, cancellationToken);

            using RentedBuffer rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, method, cancellationToken);

            return Encoding.UTF8.GetString(rentedArr.Span);
        }

        /// <summary>
        /// Карточки всех акций TQBR — паспортный блок securities.
        ///
        /// Источник: /engines/stock/markets/shares/boards/tqbr/securities.json?iss.meta=off
        /// Режим: ISS (публичный, без ключа).
        /// Парсер: ParsingInstrumentCardUtf8.ParseStockCards (один проход: securities).
        /// Источник продолжает отдавать блок marketdata, он не разбирается:
        /// ценовые поля — данные реального времени и в PostgreSQL не хранятся.
        /// </summary>
        public async Task<List<StockInstrumentCardDTO>> GetStockInstrumentCards(
            CancellationToken cancellationToken = default)
        {
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.Iss,
                MoexOperations.ReferenceInstrumentsFetch,
                MoexDataKinds.Instruments,
                MoexMarkets.Stock,
                MoexFlows.History);

            long operationStart = Stopwatch.GetTimestamp();
            try
            {
                const string endpoint =
                    "/engines/stock/markets/shares/boards/tqbr/securities.json?iss.meta=off";
 
                using var response = await _transport.SendAsync(
                    endpoint, cancellationToken: cancellationToken);
                using var rentedArr = await RentedBuffer.RentFromResponseAsync(
                    response, _options.BodyReadTimeout, endpoint, cancellationToken);
                List<StockInstrumentCardDTO> result = ParsingInstrumentCardUtf8.ParseStockCards(rentedArr.Span);
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

        public async Task<List<EngineDailyTableDTO>> GetEngine(
            string engine,
            CancellationToken ct)
        {
            string endpoint = $"/engines/{engine}.json";
            using RentedBuffer buffer = await RentAsync(endpoint, null, ct);
            return ParsingIssCalendar.ParseEngine(buffer.Memory);
        }

        public async Task<List<ListingIntervalDTO>> GetListing(
            string engine,
            string market,
            string? status,
            int limit,
            int start,
            CancellationToken ct)
        {
            string endpoint = $"/history/engines/{engine}/markets/{market}/listing.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
                ["start"] = start.ToString(CultureInfo.InvariantCulture),
            };
            if (status is not null)
                queryParams["status"] = status;

            using RentedBuffer buffer = await RentAsync(endpoint, queryParams, ct);
            return ParsingIssCalendar.ParseListing(buffer.Memory);
        }

        public async Task<List<SplitWriteDTO>> GetSplits(CancellationToken ct)
        {
            const string endpoint = "/statistics/engines/stock/splits.json";
            using RentedBuffer buffer = await RentAsync(endpoint, null, ct);
            return ParsingIssCalendar.ParseSplits(buffer.Memory);
        }

        private async Task<RentedBuffer> RentAsync(
            string endpoint,
            Dictionary<string, string>? queryParams,
            CancellationToken ct)
        {
            using HttpResponseMessage response = await _transport.SendAsync(endpoint, queryParams, ct);
            return await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, ct);
        }
    }
}
