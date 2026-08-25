using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpAlgClient
    {
        private readonly MoexOptions _options;
        private readonly MoexHttpTransport _transport;

        public MoexHttpAlgClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpAlgClient> logger)
        {
            _options = options.Value;
            _transport = new MoexHttpTransport(
                httpClient,
                logger,
                _options,
                _options.ApimBaseUrl,
                MoexLogSources.Algopack,
                requiresApiKey: true);
        }

        /// <summary>
        /// Диагностическое получение исходного тела ответа ALGOPACK. Успешность статуса
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

        // <summary>
        /// Карточки всех фьючерсов RFUD — паспортный блок securities.
        ///
        /// Источник: /engines/futures/markets/forts/boards/RFUD/securities.json?iss.meta=off
        /// Режим: APIM (платный, Bearer).
        /// Парсер: ParsingInstrumentCardUtf8.ParseFuturesCards (один проход: securities).
        /// Источник продолжает отдавать блок marketdata, он не разбирается:
        /// ценовые поля — данные реального времени и в PostgreSQL не хранятся.
        /// </summary>
        public async Task<List<FuturesInstrumentCardDTO>> GetFuturesInstrumentCards(
            CancellationToken cancellationToken = default)
        {
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.Algopack,
                MoexOperations.ReferenceInstrumentsFetch,
                MoexDataKinds.Instruments,
                MoexMarkets.Futures,
                MoexFlows.History);

            long operationStart = Stopwatch.GetTimestamp();
            try
            {
                const string endpoint =
                    "/engines/futures/markets/forts/boards/RFUD/securities.json?iss.meta=off";
 
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                using var rentedArr = await RentedBuffer.RentFromResponseAsync(
                    response, _options.BodyReadTimeout, endpoint, cancellationToken);
                List<FuturesInstrumentCardDTO> result = ParsingInstrumentCardUtf8.ParseFuturesCards(rentedArr.Span);
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
        // ═══════════════════════════════════════════════════════════
        // Инфраструктура
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Обёртка над общим транспортом. Модификатор internal сохранён: метод вызывает
        /// MoexHistoryPageReader, и переводить его на транспорт напрямую незачем —
        /// транспорт принадлежит клиенту.
        /// </summary>
        internal Task<HttpResponseMessage> SendRequestAsync(
            string method,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            return _transport.SendAsync(method, queryParams, cancellationToken);
        }
    }
}
