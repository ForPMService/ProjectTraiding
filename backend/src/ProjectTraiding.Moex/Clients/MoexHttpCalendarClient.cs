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
using System.Runtime.CompilerServices;

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
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await _transport.SendAsync(
                endpoint, cancellationToken: cancellationToken);
            using var rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                List<CalendarOffDaysMarketDTO> result = ParsingCalendarUtf8.ParseOffDaysMarket(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
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
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await _transport.SendAsync(
                endpoint, cancellationToken: cancellationToken);
            using var rentedArr = await RentedBuffer.RentFromResponseAsync(
                response, _options.BodyReadTimeout, endpoint, cancellationToken);
            try
            {
                List<CalendarOffDaysMarketDTO> result = ParsingCalendarUtf8.ParseOffDaysMarket(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
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

        

    }
}
