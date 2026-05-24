using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
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

        public MoexHttpCalendarClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpCalendarClient> logger)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        // ── Выходные дни ────────────────────────────────────────────

        public async Task<List<CalendarOffDaysAllDTO>> GetOffDaysAll(
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars.json";
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
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        public async Task<List<CalendarOffDaysMarketDTO>> GetStockOffDays(
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock.json";
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
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        public async Task<List<CalendarOffDaysMarketDTO>> GetFuturesOffDays(
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/futures.json";
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
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        // ── Сессии ────────────────────────────────────────────

        public async Task<(List<CalendarStockSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types)>
            GetStockSessionWithTypes(CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/session.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                cancellationToken);
            try
            {
                var result = ParsingCalendarUtf8.ParseStockSession(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Sessions.Count, Stopwatch.GetElapsedTime(startTimestamp));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        public async Task<(List<CalendarFuturesSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types)>
            GetFuturesSessionWithTypes(CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/futures/session.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                cancellationToken);
            try
            {
                var result = ParsingCalendarUtf8.ParseFuturesSession(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Sessions.Count, Stopwatch.GetElapsedTime(startTimestamp));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
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

        // ── Фьючерсы ──────────────────────────────────

        public async Task<(List<CalendarFortsContractDTO> Forts, List<CalendarOptionsSeriesDTO> Options)>
            GetFuturesSecuritiesAll(CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/futures/securities.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                cancellationToken);
            try
            {
                var result = ParsingCalendarUtf8.ParseFuturesSecurities(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, result.Forts.Count, Stopwatch.GetElapsedTime(startTimestamp));
                return result;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        /*
        [Obsolete("Используйте GetFuturesSecuritiesAll() — один запрос вместо двух")]
        public async Task<List<CalendarFortsContractDTO>> GetFortsContracts(
            CancellationToken cancellationToken = default)
        {
            using var response = await SendRequestAsync("/calendars/futures/securities.json", cancellationToken: cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ParsingCalendar.ParseCalendarFortsContracts(doc);
        }

        [Obsolete("Используйте GetFuturesSecuritiesAll() — один запрос вместо двух")]
        public async Task<List<CalendarOptionsSeriesDTO>> GetOptionsSeries(
            CancellationToken cancellationToken = default)
        {
            using var response = await SendRequestAsync("/calendars/futures/securities.json", cancellationToken: cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ParsingCalendar.ParseCalendarOptionsSeries(doc);
        }
        */

        // ── Приостановленные (с cursor-пагинацией) ─────────────────────

        public async Task<List<CalendarSuspendedReasonDTO>> GetSuspendedReasons(
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/suspended/details.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                cancellationToken);
            try
            {
                var (_, reasons, _) = ParsingCalendarUtf8.ParseSuspendedWithReasons(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, reasons.Count, Stopwatch.GetElapsedTime(startTimestamp));
                return reasons;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        public async IAsyncEnumerable<List<CalendarSuspendedDTO>> GetSuspended(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/suspended/details.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<CalendarSuspendedDTO> page;
                PaginationCursorDTO cursor;
                try
                {
                    var parsed = ParsingCalendarUtf8.ParseSuspendedWithReasons(rentedArr.Span);
                    page = parsed.Item1;
                    cursor = parsed.Item3;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                    throw;
                }
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
        }

        // ── Изменения по ценным бумагам (с cursor-пагинацией) ──────────────

        public async Task<List<CalendarSecurityAttributeDTO>> GetSecurityAttributes(
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/changes.json";
            long startTimestamp = Stopwatch.GetTimestamp();
            using var response = await SendRequestAsync(endpoint, cancellationToken: cancellationToken);
            int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
            using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLength,
                cancellationToken);
            try
            {
                var (_, attributes, _) = ParsingCalendarUtf8.ParseSecurityChangesWithAttributes(rentedArr.Span);
                MoexLogMessages.SinglePageReceived(_logger, endpoint, attributes.Count, Stopwatch.GetElapsedTime(startTimestamp));
                return attributes;
            }
            catch (MoexSchemaMismatchException ex)
            {
                MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                throw;
            }
        }

        public async IAsyncEnumerable<List<CalendarSecurityChangeDTO>> GetSecurityChanges(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const string endpoint = "/calendars/stock/securities/changes.json";
            Dictionary<string, string> queryParams = new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(endpoint, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<CalendarSecurityChangeDTO> page;
                PaginationCursorDTO cursor;
                try
                {
                    var parsed = ParsingCalendarUtf8.ParseSecurityChangesWithAttributes(rentedArr.Span);
                    page = parsed.Item1;
                    cursor = parsed.Item3;
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, endpoint, "schema_mismatch", ex.Message);
                    throw;
                }
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
