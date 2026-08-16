using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Diagnostics;


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
        /// Карточки всех акций TQBR — securities + marketdata одним запросом.
        ///
        /// Источник: /engines/stock/markets/shares/boards/tqbr/securities.json?iss.meta=off
        /// Режим: ISS (публичный, без ключа).
        /// Парсер: ParsingInstrumentCardUtf8.ParseStockCards (два прохода: securities + marketdata).
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
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    _options.BodyReadTimeout,
                    endpoint,
                    cancellationToken);
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
    }
}
