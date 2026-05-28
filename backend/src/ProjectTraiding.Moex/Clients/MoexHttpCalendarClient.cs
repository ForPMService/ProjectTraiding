using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.RawCapture;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpCalendarClient
    {
        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexHttpCalendarClient> _logger;
        private readonly MoexRawCaptureWriter _captureWriter;

        public MoexHttpCalendarClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpCalendarClient> logger,
            MoexRawCaptureWriter captureWriter)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
            _captureWriter = captureWriter;
        }

        // ── Выходные дни (capture-enabled) ────────────────────────────────

        public async Task<List<CalendarOffDaysAllDTO>> GetOffDaysAll(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    List<CalendarOffDaysAllDTO> result = ParsingCalendarUtf8.ParseOffDaysAll(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.OffDaysAll,
                        null,
                        null,
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
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.OffDaysAll,
                            null,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.OffDaysAll,
                        null,
                        null,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        effectiveRunId,
                        RawCaptureKeyBuilder.ResponseFileName());
                    await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                }
                throw;
            }
        }

        public async Task<List<CalendarOffDaysMarketDTO>> GetStockOffDays(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    List<CalendarOffDaysMarketDTO> result = ParsingCalendarUtf8.ParseOffDaysMarket(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.OffDays,
                        RawCaptureMarkets.Stock,
                        null,
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
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.OffDays,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.OffDays,
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

        public async Task<List<CalendarOffDaysMarketDTO>> GetFuturesOffDays(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/futures.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    List<CalendarOffDaysMarketDTO> result = ParsingCalendarUtf8.ParseOffDaysMarket(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.OffDays,
                        RawCaptureMarkets.Futures,
                        null,
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
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.OffDays,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.OffDays,
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

        // ── Сессии (capture-enabled) ────────────────────────────────────

        public async Task<(List<CalendarStockSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types)>
            GetStockSessionWithTypes(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/session.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    (List<CalendarStockSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types) result =
                        ParsingCalendarUtf8.ParseStockSession(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Sessions.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.Sessions,
                        RawCaptureMarkets.Stock,
                        null,
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
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.Sessions,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.Sessions,
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

        public async Task<(List<CalendarFuturesSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types)>
            GetFuturesSessionWithTypes(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/futures/session.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    (List<CalendarFuturesSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types) result =
                        ParsingCalendarUtf8.ParseFuturesSession(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Sessions.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.Sessions,
                        RawCaptureMarkets.Futures,
                        null,
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
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.Sessions,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.Sessions,
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

        // ── B9.5: закомментированы — заменены на GetStockSessionWithTypes/GetFuturesSessionWithTypes/GetFuturesSecuritiesAll ──
        /*
        [Obsolete("Используйте GetStockSessionWithTypes() — один запрос вместо двух")]
        public async Task<List<CalendarStockSessionDTO>> GetStockSession(
            CancellationToken cancellationToken = default)
        {
            using var response = await SendRequestAsync("/calendars/stock/session.json", cancellationToken: cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ParsingCalendar.ParseCalendarStockSession(doc);
        }
 
        [Obsolete("Используйте GetStockSessionWithTypes() — один запрос вместо двух")]
        public async Task<List<CalendarSessionTypeDTO>> GetStockSessionTypes(
            CancellationToken cancellationToken = default)
        {
            using var response = await SendRequestAsync("/calendars/stock/session.json", cancellationToken: cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ParsingCalendar.ParseCalendarSessionTypes(doc);
        }
 
        [Obsolete("Используйте GetFuturesSessionWithTypes() — один запрос вместо двух")]
        public async Task<List<CalendarFuturesSessionDTO>> GetFuturesSession(
            CancellationToken cancellationToken = default)
        {
            using var response = await SendRequestAsync("/calendars/futures/session.json", cancellationToken: cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ParsingCalendar.ParseCalendarFuturesSession(doc);
        }
 
        [Obsolete("Используйте GetFuturesSessionWithTypes() — один запрос вместо двух")]
        public async Task<List<CalendarSessionTypeDTO>> GetFuturesSessionTypes(
            CancellationToken cancellationToken = default)
        {
            using var response = await SendRequestAsync("/calendars/futures/session.json", cancellationToken: cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ParsingCalendar.ParseCalendarSessionTypes(doc);
        }
        */

        // ── Фьючерсы (capture-enabled) ──────────────────────────────────

        public async Task<(List<CalendarFortsContractDTO> Forts, List<CalendarOptionsSeriesDTO> Options)>
            GetFuturesSecuritiesAll(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/futures/securities.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    (List<CalendarFortsContractDTO> Forts, List<CalendarOptionsSeriesDTO> Options) result =
                        ParsingCalendarUtf8.ParseFuturesSecurities(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Forts.Count + result.Options.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.FortsContracts,
                        RawCaptureMarkets.Futures,
                        null,
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
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.FortsContracts,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.FortsContracts,
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

        // ── Приостановки торгов (capture-enabled) ──────────────────────────────

        public async Task<List<CalendarSuspendedReasonDTO>> GetSuspendedReasons(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/suspended/details.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    (List<CalendarSuspendedDTO> _, List<CalendarSuspendedReasonDTO> reasons, PaginationCursorDTO _) =
                        ParsingCalendarUtf8.ParseSuspendedWithReasons(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, reasons.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.SuspendedReasons,
                        RawCaptureMarkets.Stock,
                        null,
                        effectiveRunId,
                        rentedArr.Memory,
                        cancellationToken);
                    return reasons;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                    if (_captureWriter.IsEnabled)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.SchemaMismatch,
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.SuspendedReasons,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.SuspendedReasons,
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

        public async IAsyncEnumerable<List<CalendarSuspendedDTO>> GetSuspended(
            string? runId = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/suspended/details.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>();
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            using var accumulator = new RawCaptureAccumulator(_captureWriter);
            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                // Фаза 1: HTTP-запрос — capture HTTP-ошибок
                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.Suspended,
                            RawCaptureMarkets.Stock,
                            null,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                // Фаза 2: чтение + парсинг — capture schema-ошибок
                List<CalendarSuspendedDTO> page;
                PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        cancellationToken);
                    try
                    {
                        var parsed = ParsingCalendarUtf8.ParseSuspendedWithReasons(rentedArr.Span);
                        page = parsed.Item1;
                        cursor = parsed.Item3;
                        accumulator.AppendPage(rentedArr.Memory);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Calendar,
                                RawCaptureDataTypes.Suspended,
                                RawCaptureMarkets.Stock,
                                null,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
                }

                // Фаза 3: yield + пагинация (вне try-catch)
                pagesElapsed++;
                totalRows += page.Count;
                MoexLogMessages.PageReceived(_logger, endpoint, pagesElapsed, page.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return page;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, endpoint, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }

            await accumulator.FlushNdjsonAsync(
                RawCaptureClients.Calendar,
                RawCaptureDataTypes.Suspended,
                RawCaptureMarkets.Stock,
                null,
                effectiveRunId,
                cancellationToken);
        }

        // ── Изменения по ценным бумагам (capture-enabled, cursor-пагинация) ──────

        public async Task<List<CalendarSecurityAttributeDTO>> GetSecurityAttributes(
            string? runId = null,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/changes.json";
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                try
                {
                    (List<CalendarSecurityChangeDTO> _, List<CalendarSecurityAttributeDTO> attributes, PaginationCursorDTO _) =
                        ParsingCalendarUtf8.ParseSecurityChangesWithAttributes(rentedArr.Span);
                    MoexLogMessages.SinglePageReceived(_logger, endpoint, attributes.Count, Stopwatch.GetElapsedTime(startTimestamp));
                    await RawCaptureHelper.CaptureSingleAsync(
                        _captureWriter,
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.SecurityAttributes,
                        RawCaptureMarkets.Stock,
                        null,
                        effectiveRunId,
                        rentedArr.Memory,
                        cancellationToken);
                    return attributes;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                    if (_captureWriter.IsEnabled)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.SchemaMismatch,
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.SecurityAttributes,
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
                        RawCaptureClients.Calendar,
                        RawCaptureDataTypes.SecurityAttributes,
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

        public async IAsyncEnumerable<List<CalendarSecurityChangeDTO>> GetSecurityChanges(
            string? runId = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/changes.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>();
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            using var accumulator = new RawCaptureAccumulator(_captureWriter);
            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                // Фаза 1: HTTP-запрос — capture HTTP-ошибок
                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Calendar,
                            RawCaptureDataTypes.SecurityChanges,
                            RawCaptureMarkets.Stock,
                            null,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                // Фаза 2: чтение + парсинг — capture schema-ошибок
                List<CalendarSecurityChangeDTO> page;
                PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        cancellationToken);
                    try
                    {
                        var parsed = ParsingCalendarUtf8.ParseSecurityChangesWithAttributes(rentedArr.Span);
                        page = parsed.Item1;
                        cursor = parsed.Item3;
                        accumulator.AppendPage(rentedArr.Memory);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Calendar,
                                RawCaptureDataTypes.SecurityChanges,
                                RawCaptureMarkets.Stock,
                                null,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
                }

                // Фаза 3: yield + пагинация (вне try-catch)
                pagesElapsed++;
                totalRows += page.Count;
                MoexLogMessages.PageReceived(_logger, endpoint, pagesElapsed, page.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return page;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, endpoint, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }

            await accumulator.FlushNdjsonAsync(
                RawCaptureClients.Calendar,
                RawCaptureDataTypes.SecurityChanges,
                RawCaptureMarkets.Stock,
                null,
                effectiveRunId,
                cancellationToken);
        }

        // ── Инфраструктура ──────────────────────────────────────

        private async Task<HttpResponseMessage> SendRequestAsync(
            string method,
            Dictionary<string, string>? queryParams = null,
            CancellationToken cancellationToken = default)
        {
            string requestUrl = _options.ApimBaseUrl + method;
            queryParams ??= new Dictionary<string, string>();
            if (queryParams.Count > 0)
            {
                QueryString queryString = QueryString.Create(queryParams);
                requestUrl += queryString.ToString();
            }
            EnsureApiKeyConfigured();
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.AlgKey}");
            try
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await HttpClientHelpers.EnsureSuccessOrThrowAsync(response, method, cancellationToken);
                return response;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX request timeout for {method}", method, "http_client", _options.RequestTimeout, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Calendar, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutRejectedException ex)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX attempt timeout for {method}", method, "polly_attempt", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Calendar, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (MoexHttpException ex)
            {
                MoexLogMessages.RequestFailed(_logger, ex, MoexLogSources.Calendar, method, ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode, null, ex.Message);
                throw;
            }
        }

        private void EnsureApiKeyConfigured()
        {
            if (string.IsNullOrWhiteSpace(_options.AlgKey))
            {
                throw new InvalidOperationException(
                    "MOEX ALGOPACK API key is not configured. Set MoexAlg:Key via user-secrets or environment variable.");
            }
        }
    }
}
