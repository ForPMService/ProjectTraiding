using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpCalendarClient
    {
        private readonly MoexOptions _options;
        private readonly ILogger<MoexHttpCalendarClient> _logger;
        private readonly MoexHttpTransport _transport;

        public MoexHttpCalendarClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpCalendarClient> logger)
        {
            _options = options.Value;
            _logger = logger;
            _transport = new MoexHttpTransport(
                httpClient,
                logger,
                _options,
                _options.ApimBaseUrl,
                MoexLogSources.Calendar,
                requiresApiKey: true);
        }

        /// <summary>
        /// Диагностическое получение исходного тела ответа календаря. Успешность статуса
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

        public async Task<List<CalendarOffDaysMarketDTO>> GetStockOffDays(
            CancellationToken cancellationToken = default)
        {
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.Calendar,
                MoexOperations.CalendarOffDaysFetch,
                MoexDataKinds.OffDays,
                MoexMarkets.Stock,
                MoexFlows.History);

            using Activity? activity =
                MoexTelemetry.ActivitySource.StartActivity("moex.history.fetch");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Calendar);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Stock);

            long operationStart = Stopwatch.GetTimestamp();
            try
            {
            const string endpoint = "/calendars/stock.json";
            using var response = await _transport.SendAsync(
                endpoint, cancellationToken: cancellationToken);
            using var rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                List<CalendarOffDaysMarketDTO> result = ParsingCalendar.ParseOffDaysMarket(rentedArr.Memory);
                MoexMetrics.RecordOperationSuccess(
                    in operationTags, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
            }
            catch (OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                MoexMetrics.RecordOperationCancelled(
                    in operationTags, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                MoexMetrics.RecordOperationError(
                    in operationTags, ex, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                throw;
            }
        }

        public async Task<List<CalendarOffDaysMarketDTO>> GetFuturesOffDays(
            CancellationToken cancellationToken = default)
        {
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.Calendar,
                MoexOperations.CalendarOffDaysFetch,
                MoexDataKinds.OffDays,
                MoexMarkets.Futures,
                MoexFlows.History);

            using Activity? activity =
                MoexTelemetry.ActivitySource.StartActivity("moex.history.fetch");
            activity?.SetTag(MoexTelemetryAttributes.Source, MoexLogSources.Calendar);
            activity?.SetTag(MoexTelemetryAttributes.DataKind, MoexDataKinds.OffDays);
            activity?.SetTag(MoexTelemetryAttributes.Market, MoexMarkets.Futures);

            long operationStart = Stopwatch.GetTimestamp();
            try
            {
            const string endpoint = "/calendars/futures.json";
            using var response = await _transport.SendAsync(
                endpoint, cancellationToken: cancellationToken);
            using var rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                List<CalendarOffDaysMarketDTO> result = ParsingCalendar.ParseOffDaysMarket(rentedArr.Memory);
                MoexMetrics.RecordOperationSuccess(
                    in operationTags, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, MoexErrorTypes.SchemaMismatch, ex.Message);
                throw;
            }
            }
            catch (OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                MoexMetrics.RecordOperationCancelled(
                    in operationTags, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                MoexMetrics.RecordOperationError(
                    in operationTags, ex, Stopwatch.GetElapsedTime(operationStart).TotalSeconds);
                throw;
            }
        }

        public async Task<List<CalendarOffDaysMarketDTO>> GetStockOffDays(
            DateOnly from,
            DateOnly till,
            CancellationToken ct)
        {
            const string endpoint = "/calendars/stock.json";
            using RentedBuffer buffer = await RentAsync(
                endpoint, CreateDateRangeQuery(from, till, true), ct);
            return ParsingCalendar.ParseOffDaysMarket(buffer.Memory);
        }

        public async Task<List<CalendarOffDaysMarketDTO>> GetFuturesOffDays(
            DateOnly from,
            DateOnly till,
            CancellationToken ct)
        {
            const string endpoint = "/calendars/futures.json";
            using RentedBuffer buffer = await RentAsync(
                endpoint, CreateDateRangeQuery(from, till, true), ct);
            return ParsingCalendar.ParseOffDaysMarket(buffer.Memory);
        }

        public async Task<List<FuturesExpirationDTO>>
            GetFuturesSecurities(DateOnly from, DateOnly till, CancellationToken ct)
        {
            const string endpoint = "/calendars/futures/securities.json";
            using RentedBuffer buffer = await RentAsync(
                endpoint, CreateDateRangeQuery(from, till, false), ct);
            return ParsingCalendar.ParseFuturesSecurities(buffer.Memory);
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

        private static Dictionary<string, string> CreateDateRangeQuery(
            DateOnly from,
            DateOnly till,
            bool showAllDays)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["till"] = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
            if (showAllDays)
                queryParams["show_all_days"] = "1";
            return queryParams;
        }

    }
}
