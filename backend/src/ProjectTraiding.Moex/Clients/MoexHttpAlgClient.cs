using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
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
using ProjectTraiding.Moex.Infrastructure.RawCapture;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpAlgClient
    {
        private readonly MoexOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexHttpAlgClient> _logger;
        private readonly MoexRawCaptureWriter _captureWriter;

        public MoexHttpAlgClient(
            IOptions<MoexOptions> options,
            HttpClient httpClient,
            ILogger<MoexHttpAlgClient> logger,
            MoexRawCaptureWriter captureWriter)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
            _captureWriter = captureWriter;
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

        // ═══════════════════════════════════════════════════════════
        // GetCandles — fixed-page пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<CandlesDTO>> GetCandles(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? captureMarket = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int queryStart = 0;
            queryParams ??= new Dictionary<string, string>();

            if (queryParams.TryGetValue("start", out string? start) && int.TryParse(start, out int parseValue))
            {
                queryStart = parseValue;
            }

            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            int pagesElapsed = 0;
            int totalRows = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(method, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Alg,
                            RawCaptureDataTypes.Candles,
                            captureMarket, secid,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                List<CandlesDTO> candlesList;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        cancellationToken);
                    try
                    {
                        candlesList = ParsingAlgUtf8.ParseAlgCandles(rentedArr.Span);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Alg,
                                RawCaptureDataTypes.Candles,
                                captureMarket, secid,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
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

        // ═══════════════════════════════════════════════════════════
        // SuperCandles — cursor-пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<SuperCandlesTradeStats5mDTO>> GetSuperCandlesTradeStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(method, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Alg,
                            RawCaptureDataTypes.TradeStats,
                            RawCaptureMarkets.Stock, secid,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                List<SuperCandlesTradeStats5mDTO> tradeStats;
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
                        tradeStats = ParsingAlgUtf8.ParseTradeStatsStock(rentedArr.Span, out cursor);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Alg,
                                RawCaptureDataTypes.TradeStats,
                                RawCaptureMarkets.Stock, secid,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
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

        public async IAsyncEnumerable<List<SuperCandlesFuturesTradeStats5mDTO>> GetSuperCandlesFuturesTradeStats5m(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

            int pagesElapsed = 0;
            int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();

                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.TradeStats, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                List<SuperCandlesFuturesTradeStats5mDTO> tradeStats;
                PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { tradeStats = ParsingAlgUtf8.ParseTradeStatsFutures(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.TradeStats, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
                }

                pagesElapsed++;
                totalRows += tradeStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, tradeStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return tradeStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<SuperCandlesOrderBookStats5mDTO>> GetSuperCandlesOrderBookStats5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Guid.NewGuid().ToString("N");
            int pagesElapsed = 0; int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.OBStats, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken); }
                    throw;
                }
                List<SuperCandlesOrderBookStats5mDTO> orderBookStats; PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { orderBookStats = ParsingAlgUtf8.ParseOBStatsStock(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.OBStats, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken); }
                        throw;
                    }
                }
                pagesElapsed++; totalRows += orderBookStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, orderBookStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return orderBookStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<SuperCandlesFuturesOrderBookStats5mDTO>> GetSuperCandlesFuturesOrderBookStats5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Guid.NewGuid().ToString("N");
            int pagesElapsed = 0; int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.OBStats, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken); }
                    throw;
                }
                List<SuperCandlesFuturesOrderBookStats5mDTO> orderBookStats; PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { orderBookStats = ParsingAlgUtf8.ParseOBStatsFutures(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.OBStats, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken); }
                        throw;
                    }
                }
                pagesElapsed++; totalRows += orderBookStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, orderBookStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return orderBookStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<SuperCandlesOrderStats5mDTO>> GetSuperCandlesOrderStats5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Guid.NewGuid().ToString("N");
            int pagesElapsed = 0; int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.OrderStats, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken); }
                    throw;
                }
                List<SuperCandlesOrderStats5mDTO> orderStats; PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { orderStats = ParsingAlgUtf8.ParseOrderStatsStock(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.OrderStats, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken); }
                        throw;
                    }
                }
                pagesElapsed++; totalRows += orderStats.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, orderStats.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return orderStats;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // FUTOI — day-split пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<FutoiDTO>> StreamFutoi(
            string method,
            Dictionary<string, string>? queryParams = null,
            string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId
                ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                + "-" + Guid.NewGuid().ToString("N");

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

                HttpResponseMessage response;
                try
                {
                    response = await SendRequestAsync(method, queryParams, cancellationToken);
                }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    {
                        string key = RawCaptureKeyBuilder.BuildErrorKey(
                            RawCaptureErrorTypes.HttpError,
                            RawCaptureClients.Alg,
                            RawCaptureDataTypes.Futoi,
                            RawCaptureMarkets.Futures, secid,
                            DateOnly.FromDateTime(DateTime.UtcNow),
                            effectiveRunId,
                            RawCaptureKeyBuilder.DateFileName(DateOnly.FromDateTime(date)));
                        await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken);
                    }
                    throw;
                }

                List<FutoiDTO> page;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        contentLength,
                        cancellationToken);
                    try
                    {
                        page = ParsingAlgUtf8.ParseFutoi(rentedArr.Span);
                    }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        {
                            string key = RawCaptureKeyBuilder.BuildErrorKey(
                                RawCaptureErrorTypes.SchemaMismatch,
                                RawCaptureClients.Alg,
                                RawCaptureDataTypes.Futoi,
                                RawCaptureMarkets.Futures, secid,
                                DateOnly.FromDateTime(DateTime.UtcNow),
                                effectiveRunId,
                                RawCaptureKeyBuilder.DateFileName(DateOnly.FromDateTime(date)));
                            await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken);
                        }
                        throw;
                    }
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

        // ═══════════════════════════════════════════════════════════
        // HI2 — cursor-пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<Hi2AssetDTO>> GetHi2Asset5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Guid.NewGuid().ToString("N");
            int pagesElapsed = 0; int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.Hi2, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken); }
                    throw;
                }
                List<Hi2AssetDTO> hi2Assets; PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { hi2Assets = ParsingAlgUtf8.ParseHi2Stock(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.Hi2, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken); }
                        throw;
                    }
                }
                pagesElapsed++; totalRows += hi2Assets.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, hi2Assets.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return hi2Assets;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<Hi2FuturesDTO>> GetHi2Furures5m(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Guid.NewGuid().ToString("N");
            int pagesElapsed = 0; int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.Hi2, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken); }
                    throw;
                }
                List<Hi2FuturesDTO> hi2Futures; PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { hi2Futures = ParsingAlgUtf8.ParseHi2Futures(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.Hi2, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken); }
                        throw;
                    }
                }
                pagesElapsed++; totalRows += hi2Futures.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, hi2Futures.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return hi2Futures;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // MegaAlerts — cursor-пагинация (capture-enabled)
        // ═══════════════════════════════════════════════════════════

        public async IAsyncEnumerable<List<MegaAlertsAssetsDTO>> GetMegaAlerts(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Guid.NewGuid().ToString("N");
            int pagesElapsed = 0; int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.MegaAlerts, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken); }
                    throw;
                }
                List<MegaAlertsAssetsDTO> megaAlerts; PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { megaAlerts = ParsingAlgUtf8.ParseMegaAlertsStock(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.MegaAlerts, RawCaptureMarkets.Stock, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken); }
                        throw;
                    }
                }
                pagesElapsed++; totalRows += megaAlerts.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, megaAlerts.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return megaAlerts;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        public async IAsyncEnumerable<List<MegaAlertsFuturesDTO>> GetMegaAlertsFutures(
            string method, Dictionary<string, string>? queryParams = null, string? runId = null,
            string? secid = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            queryParams ??= new Dictionary<string, string>();
            string effectiveRunId = runId ?? "manual-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Guid.NewGuid().ToString("N");
            int pagesElapsed = 0; int totalRows = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pageStart = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                try { response = await SendRequestAsync(method, queryParams, cancellationToken); }
                catch (MoexHttpException ex)
                {
                    if (_captureWriter.IsEnabled && ex.ErrorBody is not null)
                    { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.HttpError, RawCaptureClients.Alg, RawCaptureDataTypes.MegaAlerts, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, ex.ErrorBody, cancellationToken); }
                    throw;
                }
                List<MegaAlertsFuturesDTO> megaAlertsFutures; PaginationCursorDTO cursor;
                using (response)
                {
                    int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
                    using var rentedArr = await RentedBuffer.RentFromStreamAsync(await response.Content.ReadAsStreamAsync(cancellationToken), contentLength, cancellationToken);
                    try { megaAlertsFutures = ParsingAlgUtf8.ParseMegaAlertsFutures(rentedArr.Span, out cursor); }
                    catch (MoexSchemaMismatchException ex)
                    {
                        MoexLogMessages.ParseFailed(_logger, ex, method, "schema_mismatch", ex.Message);
                        if (_captureWriter.IsEnabled)
                        { string key = RawCaptureKeyBuilder.BuildErrorKey(RawCaptureErrorTypes.SchemaMismatch, RawCaptureClients.Alg, RawCaptureDataTypes.MegaAlerts, RawCaptureMarkets.Futures, secid, DateOnly.FromDateTime(DateTime.UtcNow), effectiveRunId, RawCaptureKeyBuilder.PageFileName(pagesElapsed + 1)); await _captureWriter.TryCaptureAsync(key, rentedArr.Memory, cancellationToken); }
                        throw;
                    }
                }
                pagesElapsed++; totalRows += megaAlertsFutures.Count;
                MoexLogMessages.PageReceived(_logger, method, pagesElapsed, megaAlertsFutures.Count, Stopwatch.GetElapsedTime(pageStart));
                yield return megaAlertsFutures;
                PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed, _options.MaxPagesPerLoad);
                if (step.IsStop) { MoexLogMessages.PaginationStopped(_logger, method, step.StopReason!, pagesElapsed, totalRows); break; }
                queryParams["start"] = step.NextStart.ToString();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Инфраструктура
        // ═══════════════════════════════════════════════════════════

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
                await HttpClientHelpers.EnsureSuccessOrThrowAsync(response, method, cancellationToken);
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
