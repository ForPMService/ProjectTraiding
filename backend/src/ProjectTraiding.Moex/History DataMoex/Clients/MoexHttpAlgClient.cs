using History_DataMoex.Clients.Errors;
using History_DataMoex.Contracts.Dto;
using History_DataMoex.Contracts.Dto.Algopack;
using History_DataMoex.Contracts.Pagination;
using History_DataMoex.Infrastructure.Buffers;
using History_DataMoex.Options;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

namespace History_DataMoex.Clients
{
    public class MoexHttpAlgClient
    {
        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexHttpAlgClient> _logger;

        public MoexHttpAlgClient(IOptions<MoexOptions> options, HttpClient httpClient, ILogger<MoexHttpAlgClient> logger)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> GetRaw(
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
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.AlgKey}");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            // Не бросаем — видим что MOEX ответил
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async IAsyncEnumerable<List<CandlesDTO>> GetCandles(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int queryStart = 0;
            queryParams ??= new Dictionary<string, string>();

            if (queryParams.TryGetValue("start", out string? start) && int.TryParse(start, out int parseValue)) 
            {
                queryStart = parseValue;
            }

            int pagesElapsed = 0;
            int totalRows = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);
                List<CandlesDTO> candlesList;
                try
                {
                    candlesList = ParsingAlgUtf8.ParseAlgCandles(rentedArr.Span);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += candlesList.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, candlesList.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return candlesList;
                if (candlesList.Count >= 500)
                {
                    queryStart += 500;
                    queryParams["start"] = queryStart.ToString();
                }
                else
                {
                    MoexLogMessages.FixedPagePaginationStopped(_logger, method, "last_page_incomplete", pagesElapsed, totalRows, candlesList.Count, 500);
                    break;
                }
            }
        }

        

        public async IAsyncEnumerable<List<Hi2AssetDTO>> GetHi2Asset5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {

            queryParams ??= new Dictionary<string, string>();

            

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<Hi2AssetDTO> hi2Assets;
                PaginationCursorDTO cursor;
                try
                {
                    hi2Assets = ParsingAlgUtf8.ParseHi2Stock(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += hi2Assets.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, hi2Assets.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return hi2Assets;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<Hi2FuturesDTO>> GetHi2Furures5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {

            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<Hi2FuturesDTO> hi2Futures;
                PaginationCursorDTO cursor;
                try
                {
                    hi2Futures = ParsingAlgUtf8.ParseHi2Futures(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += hi2Futures.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, hi2Futures.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return hi2Futures;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<MegaAlertsAssetsDTO>> GetMegaAlerts(
            string metod,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(metod, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<MegaAlertsAssetsDTO> megaAlerts;
                PaginationCursorDTO cursor;
                try
                {
                    megaAlerts = ParsingAlgUtf8.ParseMegaAlertsStock(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, metod, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += megaAlerts.Count;
                MoexLogMessages.PageReceived(_logger, metod, pagesElapsed, megaAlerts.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return megaAlerts;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, metod, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }
        public async IAsyncEnumerable<List<MegaAlertsFuturesDTO>> GetMegaAlertsFutures(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<MegaAlertsFuturesDTO> megaAlertsFutures;
                PaginationCursorDTO cursor;
                try
                {
                    megaAlertsFutures = ParsingAlgUtf8.ParseMegaAlertsFutures(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += megaAlertsFutures.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, megaAlertsFutures.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return megaAlertsFutures;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<SuperCandlesTradeStats5mDTO>> GetSuperCandlesTradeStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            
            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<SuperCandlesTradeStats5mDTO> tradeStats;
                PaginationCursorDTO cursor;
                try
                {
                    tradeStats = ParsingAlgUtf8.ParseTradeStatsStock(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += tradeStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, tradeStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return tradeStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<SuperCandlesOrderBookStats5mDTO>> GetSuperCandlesOrderBookStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {

            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<SuperCandlesOrderBookStats5mDTO> orderBookStats;
                PaginationCursorDTO cursor;
                try
                {
                    orderBookStats = ParsingAlgUtf8.ParseOBStatsStock(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += orderBookStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, orderBookStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return orderBookStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<SuperCandlesOrderStats5mDTO>> GetSuperCandlesOrderStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {

            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<SuperCandlesOrderStats5mDTO> orderStats;
                PaginationCursorDTO cursor;
                try
                {
                    orderStats = ParsingAlgUtf8.ParseOrderStatsStock(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += orderStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, orderStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return orderStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<SuperCandlesFuturesOrderBookStats5mDTO>> GetSuperCandlesFuturesOrderBookStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<SuperCandlesFuturesOrderBookStats5mDTO> orderBookStats;
                PaginationCursorDTO cursor;
                try
                {
                    orderBookStats = ParsingAlgUtf8.ParseOBStatsFutures(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += orderBookStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, orderBookStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return orderBookStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<FutoiDTO>> StreamFutoi(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();

            // FUTOI API не поддерживает пагинацию (start игнорируется, лимит 1000 строк).
            // Разбиваем диапазон дат по одному дню — один день Si ≈ 470 строк, всегда < 1000.

            if (!queryParams.TryGetValue("from", out string? fromStr)
                || !queryParams.TryGetValue("till", out string? tillStr))
            {
                throw new InvalidOperationException(
                    "StreamFutoi requires 'from' and 'till' in queryParams.");
            }

            if (!DateTime.TryParseExact(fromStr, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime fromDate)
                || !DateTime.TryParseExact(tillStr, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime tillDate))
            {
                throw new InvalidOperationException(
                    $"StreamFutoi: invalid date format. from='{fromStr}', till='{tillStr}'. Expected yyyy-MM-dd.");
            }

            // Убрать start/offset если были — MOEX их игнорирует
            queryParams.Remove("start");
            queryParams.Remove("offset");

            int dayIndex = 0;
            int totalRows = 0;

            for (DateTime date = fromDate; date <= tillDate; date = date.AddDays(1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                queryParams["from"] = date.ToString("yyyy-MM-dd");
                queryParams["till"] = date.ToString("yyyy-MM-dd");

                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<FutoiDTO> page;
                try
                {
                    page = ParsingAlgUtf8.ParseFutoi(rentedArr.Span);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                dayIndex++;
                totalRows += page.Count;
                MoexLogMessages.DaySplitPageReceived(_logger, method, date.ToString("yyyy-MM-dd"), page.Count, Stopwatch.GetElapsedTime(pageStart));

                if (page.Count > 0)
                {
                    yield return page;
                }
            }
            MoexLogMessages.DaySplitCompleted(_logger, method, fromStr, tillStr, dayIndex, totalRows);
        }

        public async IAsyncEnumerable<List<SuperCandlesFuturesTradeStats5mDTO>> GetSuperCandlesFuturesTradeStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                using var response = await SendRequestAsync(method, queryParams, cancellationToken);
                int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    contentLength,
                    cancellationToken);

                List<SuperCandlesFuturesTradeStats5mDTO> tradeStats;
                PaginationCursorDTO cursor;
                try
                {
                    tradeStats = ParsingAlgUtf8.ParseTradeStatsFutures(rentedArr.Span, out cursor);
                }
                catch (MoexSchemaMismatchException ex)
                {
                    MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                    throw;
                }
                pagesElapsed++;
                totalRows += tradeStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, tradeStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return tradeStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop)
                {
                    MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows);
                    break;
                }
                queryParams["start"] = step.NextStart.ToString();
            }
        }


        private async Task<HttpResponseMessage> SendRequestAsync(string method, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default)
        {
            string baseUrl = _options.ApimBaseUrl;
            string requestUrl = baseUrl + method;
            queryParams ??= new Dictionary<string, string>();
            if (queryParams.Count > 0)
            {
                QueryString queryString = QueryString.Create(queryParams!);
                requestUrl += queryString.ToString();
            }
            EnsureApiKeyConfigured();
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.AlgKey}");
            try
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                HttpClientHelpers.EnsureSuccessOrThrow(response, method);
                return response;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX request timeout for {method}", method, "http_client", _options.RequestTimeout, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Algopack, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutRejectedException ex)
            {
                var timeoutEx = new MoexTimeoutException($"MOEX attempt timeout for {method}", method, "polly_attempt", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, MoexLogSources.Algopack, method, timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (MoexHttpException ex)
            {
                MoexLogMessages.RequestFailed(_logger, ex, MoexLogSources.Algopack, method, ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode, null, ex.Message);
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
