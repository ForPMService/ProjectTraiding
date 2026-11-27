using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.Receiver;
using ProjectTraiding.Moex.Realtime.Subscriptions;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Diagnostics;
using Wolverine;

namespace ProjectTraiding.Moex.Realtime.CurrentDay;

/// <summary>
/// Периодически дочитывает сегодняшний московский день для рядов ALGOPACK по общему
/// списку подписок. Каждый вид данных имеет собственный срок запуска, а высокая отметка
/// ряда не даёт повторно отправлять в ClickHouse уже записанную часть дня.
/// </summary>
public sealed class AlgopackCurrentDayService : BackgroundService
{
    private const string StockMarket = "stock";
    private const string FuturesMarket = "futures";
    private const string StockBoardId = "TQBR";
    private const string FuturesBoardId = "RFUD";
    private static readonly TimeSpan TurnInterval = TimeSpan.FromSeconds(10);

    private static readonly MoexSeriesSpec[] CurrentDaySpecs =
    [
        MoexSeriesRegistry.TradeStatsStock,
        MoexSeriesRegistry.TradeStatsFutures,
        MoexSeriesRegistry.ObStatsStock,
        MoexSeriesRegistry.ObStatsFutures,
        MoexSeriesRegistry.OrderStatsStock,
        MoexSeriesRegistry.Futoi,
        MoexSeriesRegistry.MegaAlertsStock,
        MoexSeriesRegistry.MegaAlertsFutures,
        MoexSeriesRegistry.Hi2Stock,
        MoexSeriesRegistry.Hi2Futures,
    ];

    private readonly MoexReceiverInstrumentReader _instrumentReader;
    private readonly MoexHistoryPageReader _pageReader;
    private readonly ClickHouseInsertExecutor _executor;
    private readonly StreamCursorWriter _cursorWriter;
    private readonly FutoiSubjectReader _futoiSubjectReader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CandleSubscriptionRegistry _candleSubscriptions;
    private readonly ILogger<AlgopackCurrentDayService> _logger;
    private readonly TimeSpan _instrumentFetchTimeout;
    private readonly int _batchSize;
    private readonly TimeOnly _hi2DailyTime;
    private readonly TimeSpan _hi2RetryInterval;
    private readonly PeriodicKind[] _periodicKinds;
    private readonly Dictionary<Hi2InstrumentKey, long> _hi2LastAttempts = new();

    public AlgopackCurrentDayService(
        MoexReceiverInstrumentReader instrumentReader,
        MoexHistoryPageReader pageReader,
        ClickHouseInsertExecutor executor,
        StreamCursorWriter cursorWriter,
        FutoiSubjectReader futoiSubjectReader,
        IServiceScopeFactory scopeFactory,
        CandleSubscriptionRegistry candleSubscriptions,
        int batchSize,
        IOptions<MoexOptions> options,
        ILogger<AlgopackCurrentDayService> logger)
    {
        _instrumentReader = instrumentReader;
        _pageReader = pageReader;
        _executor = executor;
        _cursorWriter = cursorWriter;
        _futoiSubjectReader = futoiSubjectReader;
        _scopeFactory = scopeFactory;
        _candleSubscriptions = candleSubscriptions;
        _batchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));
        _logger = logger;

        MoexOptions value = options.Value;
        _instrumentFetchTimeout = value.RealtimeInstrumentFetchTimeout;
        _hi2DailyTime = new TimeOnly(value.Hi2DailyHour, value.Hi2DailyMinute);
        _hi2RetryInterval = TimeSpan.FromSeconds(value.Hi2RetrySeconds);
        _periodicKinds =
        [
            new PeriodicKind("tradestats", TimeSpan.FromSeconds(value.TradeStatsPollSeconds)),
            new PeriodicKind("obstats", TimeSpan.FromSeconds(value.ObStatsPollSeconds)),
            new PeriodicKind("orderstats", TimeSpan.FromSeconds(value.OrderStatsPollSeconds)),
            new PeriodicKind("futoi", TimeSpan.FromSeconds(value.FutoiPollSeconds)),
            new PeriodicKind("mega_alerts", TimeSpan.FromSeconds(value.MegaAlertsPollSeconds)),
        ];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AlgopackCurrentDayLogMessages.Started(_logger, TurnInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunTurnAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AlgopackCurrentDayLogMessages.TurnFailed(_logger, ex);
                }

                try
                {
                    await Task.Delay(TurnInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            AlgopackCurrentDayLogMessages.Stopped(_logger);
        }
    }

    private async Task RunTurnAsync(CancellationToken ct)
    {
        DateOnly today = MoexTime.Today;

        for (int i = 0; i < _periodicKinds.Length; i++)
        {
            PeriodicKind kind = _periodicKinds[i];
            long attemptTimestamp = Stopwatch.GetTimestamp();
            if (kind.HasAttempted
                && Stopwatch.GetElapsedTime(kind.LastAttemptTimestamp, attemptTimestamp) < kind.Interval)
            {
                continue;
            }

            kind.HasAttempted = true;
            kind.LastAttemptTimestamp = attemptTimestamp;

            try
            {
                await RunDataKindAsync(kind.DataKind, today, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AlgopackCurrentDayLogMessages.DataKindFailed(_logger, ex, kind.DataKind);
            }
        }

        try
        {
            await RunHi2Async(today, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AlgopackCurrentDayLogMessages.DataKindFailed(_logger, ex, "hi2");
        }
    }

    private async Task RunDataKindAsync(string dataKind, DateOnly today, CancellationToken ct)
    {
        IReadOnlyList<ReceiverInstrument> instruments =
            await _instrumentReader.GetEnabledForDataKindAsync(dataKind, ct);

        if (dataKind == "futoi")
            instruments = await ResolveFutoiSubjectsAsync(instruments, ct);

        for (int i = 0; i < instruments.Count; i++)
        {
            ReceiverInstrument instrument = instruments[i];
            MoexSeriesSpec? spec = FindSpec(dataKind, instrument.Market);
            if (spec is null)
                continue;

            try
            {
                await ProcessInstrumentAsync(
                    spec, instrument.Secid, instrument.Market, today,
                    skipIfWrittenToday: false, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AlgopackCurrentDayLogMessages.InstrumentFailed(
                    _logger, ex, dataKind, instrument.Secid, instrument.Market);
            }
        }
    }

    private async Task RunHi2Async(DateOnly today, CancellationToken ct)
    {
        DateTime moscowNow = MoexTime.Now;
        if (TimeOnly.FromDateTime(moscowNow) < _hi2DailyTime)
            return;

        IReadOnlyList<ReceiverInstrument> instruments =
            await _instrumentReader.GetEnabledForDataKindAsync("hi2", ct);

        for (int i = 0; i < instruments.Count; i++)
        {
            ReceiverInstrument instrument = instruments[i];
            MoexSeriesSpec? spec = FindSpec("hi2", instrument.Market);
            if (spec is null)
                continue;

            Hi2InstrumentKey key = new(instrument.Secid, instrument.Market);
            long attemptTimestamp = Stopwatch.GetTimestamp();
            if (_hi2LastAttempts.TryGetValue(key, out long lastAttempt)
                && Stopwatch.GetElapsedTime(lastAttempt, attemptTimestamp) < _hi2RetryInterval)
            {
                continue;
            }

            _hi2LastAttempts[key] = attemptTimestamp;

            try
            {
                await ProcessInstrumentAsync(
                    spec, instrument.Secid, instrument.Market, today,
                    skipIfWrittenToday: true, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AlgopackCurrentDayLogMessages.InstrumentFailed(
                    _logger, ex, "hi2", instrument.Secid, instrument.Market);
            }
        }
    }

    private async Task<IReadOnlyList<ReceiverInstrument>> ResolveFutoiSubjectsAsync(
        IReadOnlyList<ReceiverInstrument> instruments,
        CancellationToken ct)
    {
        List<string> contractSecids = new(instruments.Count);
        for (int i = 0; i < instruments.Count; i++)
        {
            ReceiverInstrument instrument = instruments[i];
            if (FindSpec("futoi", instrument.Market) is not null)
                contractSecids.Add(instrument.Secid);
        }

        Dictionary<string, string> resolved =
            await _futoiSubjectReader.ResolveAsync(contractSecids.ToArray(), ct);
        HashSet<string> uniqueSubjects = new(StringComparer.Ordinal);
        List<ReceiverInstrument> subjects = new(resolved.Count);

        for (int i = 0; i < contractSecids.Count; i++)
        {
            if (!resolved.TryGetValue(contractSecids[i], out string? subject)
                || !uniqueSubjects.Add(subject))
            {
                continue;
            }

            subjects.Add(new ReceiverInstrument(subject, FuturesMarket));
        }

        return subjects;
    }

    private async Task ProcessInstrumentAsync(
        MoexSeriesSpec spec,
        string secid,
        string market,
        DateOnly today,
        bool skipIfWrittenToday,
        CancellationToken ct)
    {
        string boardId = GetBoardId(market);
        StreamCursorState? cursor = await _cursorWriter.TryGetAsync(
            secid, market, boardId, spec.DataKind, spec.CandleInterval, ct);

        DateTime? lastSourceTime = cursor?.LastSourceTime;
        if (skipIfWrittenToday
            && lastSourceTime is DateTime completedTime
            && DateOnly.FromDateTime(completedTime) == today)
        {
            return;
        }

        string telemetryDataKind = MoexDataKinds.FromTaskDataKind(spec.DataKind);
        MoexOperationTags operationTags = new(
            MoexLogSources.Algopack,
            MoexOperations.RealtimeAlgopackCurrentDayPoll,
            telemetryDataKind,
            market,
            MoexFlows.Realtime);
        StorageInsertContext insertContext = new(
            telemetryDataKind,
            market,
            MoexFlows.Realtime);

        using Activity? pollActivity =
            MoexTelemetry.ActivitySource.StartActivity("moex.realtime.instrument.poll");
        pollActivity?.SetTag(MoexTelemetryAttributes.DataKind, telemetryDataKind);
        pollActivity?.SetTag(MoexTelemetryAttributes.Market, market);
        pollActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);

        try
        {
            if (spec.DataKind == "tradestats" && market == StockMarket)
            {
                StorageInsertContext candleInsertContext = new(
                    MoexDataKinds.Candles, market, MoexFlows.Realtime, 5);
                ProtobufBatchWriter<TradeStatsStockRow> statsWriter = new(
                    _executor, spec.Table, spec.Columns, insertContext);
                ProtobufBatchWriter<CandleRow> candleWriter = new(
                    _executor, MoexSeriesRegistry.TradeStatsCandleTable, CandleRow.InsertColumns,
                    candleInsertContext);
                await ProcessTradeStatsInstrumentAsync(
                    spec,
                    secid,
                    market,
                    boardId,
                    today,
                    lastSourceTime,
                    statsWriter,
                    candleWriter,
                    operationTags,
                    ct);
            }
            else if (spec.DataKind == "tradestats" && market == FuturesMarket)
            {
                StorageInsertContext candleInsertContext = new(
                    MoexDataKinds.Candles, market, MoexFlows.Realtime, 5);
                ProtobufBatchWriter<TradeStatsFuturesRow> statsWriter = new(
                    _executor, spec.Table, spec.Columns, insertContext);
                ProtobufBatchWriter<CandleRow> candleWriter = new(
                    _executor, MoexSeriesRegistry.TradeStatsCandleTable, CandleRow.InsertColumns,
                    candleInsertContext);
                await ProcessTradeStatsInstrumentAsync(
                    spec,
                    secid,
                    market,
                    boardId,
                    today,
                    lastSourceTime,
                    statsWriter,
                    candleWriter,
                    operationTags,
                    ct);
            }
            else
            {
                if (spec.DataKind == "obstats" && market == StockMarket)
                    await ProcessHistoricalInstrumentAsync<ObStatsStockRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadObStatsStockPagesAsync, ct);
                else if (spec.DataKind == "obstats" && market == FuturesMarket)
                    await ProcessHistoricalInstrumentAsync<ObStatsFuturesRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadObStatsFuturesPagesAsync, ct);
                else if (spec.DataKind == "orderstats" && market == StockMarket)
                    await ProcessHistoricalInstrumentAsync<OrderStatsStockRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadOrderStatsStockPagesAsync, ct);
                else if (spec.DataKind == "futoi" && market == FuturesMarket)
                    await ProcessHistoricalInstrumentAsync<FutoiRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadFutoiPagesAsync, ct);
                else if (spec.DataKind == "hi2" && market == StockMarket)
                    await ProcessHistoricalInstrumentAsync<Hi2StockRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadHi2StockPagesAsync, ct);
                else if (spec.DataKind == "hi2" && market == FuturesMarket)
                    await ProcessHistoricalInstrumentAsync<Hi2FuturesRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadHi2FuturesPagesAsync, ct);
                else if (spec.DataKind == "mega_alerts" && market == StockMarket)
                    await ProcessHistoricalInstrumentAsync<MegaAlertsStockRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadMegaAlertsStockPagesAsync, ct);
                else if (spec.DataKind == "mega_alerts" && market == FuturesMarket)
                    await ProcessHistoricalInstrumentAsync<MegaAlertsFuturesRow>(spec, secid, market, boardId,
                        today, lastSourceTime, new(_executor, spec.Table, spec.Columns, insertContext),
                        operationTags, _pageReader.ReadMegaAlertsFuturesPagesAsync, ct);
                else
                    throw new InvalidOperationException(
                        $"Нет типизированного обработчика текущего дня {spec.DataKind}/{market}.");
            }

            MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Success);
            pollActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            pollActivity?.SetStatus(ActivityStatusCode.Ok);
            MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
            throw;
        }
    }

    private async Task ProcessTradeStatsInstrumentAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        string secid,
        string market,
        string boardId,
        DateOnly today,
        DateTime? lastSourceTime,
        ProtobufBatchWriter<TRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        MoexOperationTags operationTags,
        CancellationToken ct)
        where TRow : struct
    {
        List<CandleRow>? acceptedCandles = _candleSubscriptions.HasSubscribers ? [] : null;
        AsyncServiceScope? publishScope = null;
        IMessageBus? messageBus = null;
        bool messageBusUnavailable = false;
        bool betweenPageFailure = false;
        HistoricalReadState state = new(_logger, operationTags, secid, lastSourceTime);
        Exception? fetchFailure = null;
        long rowsInsertedReported = 0;
        TimeSpan fetchBudgetRemaining = _instrumentFetchTimeout;

        try
        {
            using CancellationTokenSource fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Stopwatch? fetchTimer = null;

            void StopFetchTimer()
            {
                if (fetchTimer is { IsRunning: true })
                {
                    fetchTimer.Stop();
                    fetchBudgetRemaining -= fetchTimer.Elapsed;
                }
                if (!fetchCts.IsCancellationRequested)
                    fetchCts.CancelAfter(Timeout.InfiniteTimeSpan);
            }

            void BeforeFetch()
            {
                ct.ThrowIfCancellationRequested();
                if (fetchBudgetRemaining <= TimeSpan.Zero || fetchCts.IsCancellationRequested)
                {
                    AlgopackCurrentDayLogMessages.InstrumentFetchTimedOut(
                        _logger, secid, market, spec.DataKind, _instrumentFetchTimeout);
                    throw new TimeoutException("Не все страницы ALGOPACK получены до истечения времени.");
                }
                acceptedCandles?.Clear();
                fetchCts.CancelAfter(fetchBudgetRemaining);
                fetchTimer = Stopwatch.StartNew();
            }

            async ValueTask AfterPage(CancellationToken token)
            {
                StopFetchTimer();
                try
                {
                    if (acceptedCandles is { Count: > 0 } && _candleSubscriptions.HasSubscribers)
                    {
                        if (messageBus is null && !messageBusUnavailable)
                        {
                            try
                            {
                                publishScope = _scopeFactory.CreateAsyncScope();
                                messageBus = publishScope.Value.ServiceProvider.GetRequiredService<IMessageBus>();
                            }
                            catch (Exception ex)
                            {
                                messageBusUnavailable = true;
                                CandleRow firstCandle = acceptedCandles[0];
                                MoexRealtimeReceiverLogMessages.CandleAcceptedPublishFailed(
                                    _logger, ex, firstCandle.Secid, market, 5, firstCandle.Begin);
                            }
                        }

                        if (messageBus is not null)
                        {
                            for (int i = 0; i < acceptedCandles.Count; i++)
                            {
                                token.ThrowIfCancellationRequested();
                                CandleRow candle = acceptedCandles[i];
                                try
                                {
                                    await messageBus.PublishAsync(new CandleAccepted(candle, market, 5));
                                    token.ThrowIfCancellationRequested();
                                }
                                catch (OperationCanceledException) when (token.IsCancellationRequested)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    MoexRealtimeReceiverLogMessages.CandleAcceptedPublishFailed(
                                        _logger, ex, candle.Secid, market, 5, candle.Begin);
                                }
                            }
                        }
                    }
                    acceptedCandles?.Clear();

                    bool flushStats = statsWriter.RowCount >= _batchSize;
                    bool flushCandles = candleWriter.RowCount >= _batchSize;
                    if (flushStats || flushCandles)
                        rowsInsertedReported += await FlushTradeStatsWritersAsync(
                            statsWriter, candleWriter, flushStats, flushCandles, token);
                }
                catch
                {
                    betweenPageFailure = true;
                    throw;
                }
            }

            try
            {
                await _pageReader.ReadPagesAsync(
                    spec, secid, boardId: null, today, today, operationTags,
                    statsWriter, candleWriter,
                    lastSourceTime, ingestPriority: 0, acceptedCandles,
                    state, BeforeFetch, AfterPage, fetchCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (fetchCts.IsCancellationRequested && !betweenPageFailure)
            {
                StopFetchTimer();
                AlgopackCurrentDayLogMessages.InstrumentFetchTimedOut(
                    _logger, secid, market, spec.DataKind, _instrumentFetchTimeout);
                fetchFailure = new TimeoutException("Не все страницы ALGOPACK получены до истечения времени.");
            }
            catch (TimeoutException ex) when (!betweenPageFailure)
            {
                StopFetchTimer();
                fetchFailure = ex;
            }
            catch (Exception ex) when (!betweenPageFailure)
            {
                StopFetchTimer();
                fetchFailure = ex;
            }
            finally
            {
                StopFetchTimer();
                acceptedCandles?.Clear();
            }

            ct.ThrowIfCancellationRequested();
            try
            {
                rowsInsertedReported += await FlushTradeStatsWritersAsync(
                    statsWriter, candleWriter, flushStats: true, flushCandles: true, ct);
            }
            catch (Exception flushFailure) when (fetchFailure is not null && !ct.IsCancellationRequested)
            {
                throw new AggregateException(
                    "Ошибка чтения ALGOPACK и последующая ошибка отправки уже разобранных строк.",
                    fetchFailure, flushFailure);
            }
            ClickHouseWriterLogMessages.RangeWritten(_logger, secid, state.RowsRead, rowsInsertedReported);

            if (fetchFailure is null
                && state.SignificantSkippedRows == 0
                && state.MaxAcceptedSourceTime is DateTime maxSourceTime)
            {
                await _cursorWriter.UpsertAsync(
                    secid, market, boardId, spec.DataKind, spec.CandleInterval,
                    maxSourceTime, lastTradeNo: null, ct);
            }

            if (fetchFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fetchFailure).Throw();
            if (state.SignificantSkippedRows > 0)
                throw new InvalidOperationException("Данные ALGOPACK обработаны частично.");
        }
        finally
        {
            acceptedCandles?.Clear();
            if (publishScope is AsyncServiceScope scope)
                await scope.DisposeAsync();
        }
    }

    private async Task ProcessHistoricalInstrumentAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec,
        string secid,
        string market,
        string boardId,
        DateOnly today,
        DateTime? lastSourceTime,
        ProtobufBatchWriter<TRow> writer,
        MoexOperationTags operationTags,
        MoexHistoryPageReader.ReadSeriesPages<TRow> readPages,
        CancellationToken ct)
        where TRow : struct
    {
        HistoricalReadState state = new(_logger, operationTags, secid, lastSourceTime);
        Exception? fetchFailure = null;
        long rowsInsertedReported = 0;
        TimeSpan fetchBudgetRemaining = _instrumentFetchTimeout;
        bool betweenPageFailure = false;

        using (CancellationTokenSource fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            Stopwatch? fetchTimer = null;

            void StopFetchTimer()
            {
                if (fetchTimer is { IsRunning: true })
                {
                    fetchTimer.Stop();
                    fetchBudgetRemaining -= fetchTimer.Elapsed;
                }
                if (!fetchCts.IsCancellationRequested)
                    fetchCts.CancelAfter(Timeout.InfiniteTimeSpan);
            }

            void BeforeFetch()
            {
                ct.ThrowIfCancellationRequested();
                if (fetchBudgetRemaining <= TimeSpan.Zero || fetchCts.IsCancellationRequested)
                {
                    AlgopackCurrentDayLogMessages.InstrumentFetchTimedOut(
                        _logger, secid, market, spec.DataKind, _instrumentFetchTimeout);
                    throw new TimeoutException("Не все страницы ALGOPACK получены до истечения времени.");
                }
                fetchCts.CancelAfter(fetchBudgetRemaining);
                fetchTimer = Stopwatch.StartNew();
            }

            async ValueTask AfterPage(CancellationToken token)
            {
                StopFetchTimer();
                if (writer.RowCount >= _batchSize)
                {
                    try { rowsInsertedReported += await writer.FlushAsync(token); }
                    catch { betweenPageFailure = true; throw; }
                }
            }

            try
            {
                await readPages(
                    spec, secid, today, today, operationTags, writer, lastSourceTime,
                    state, BeforeFetch, AfterPage, fetchCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (fetchCts.IsCancellationRequested && !betweenPageFailure)
            {
                StopFetchTimer();
                AlgopackCurrentDayLogMessages.InstrumentFetchTimedOut(
                    _logger, secid, market, spec.DataKind, _instrumentFetchTimeout);
                fetchFailure = new TimeoutException("Не все страницы ALGOPACK получены до истечения времени.");
            }
            catch (TimeoutException ex) when (!betweenPageFailure)
            {
                StopFetchTimer();
                fetchFailure = ex;
            }
            catch (Exception ex) when (!betweenPageFailure)
            {
                StopFetchTimer();
                fetchFailure = ex;
            }
            finally
            {
                StopFetchTimer();
            }
        }

        if (writer.RowCount > 0)
        {
            try { rowsInsertedReported += await writer.FlushAsync(ct); }
            catch (Exception flushFailure) when (fetchFailure is not null && !ct.IsCancellationRequested)
            {
                throw new AggregateException(
                    "Ошибка чтения ALGOPACK и последующая ошибка отправки уже разобранных строк.",
                    fetchFailure, flushFailure);
            }
        }

        ClickHouseWriterLogMessages.RangeWritten(_logger, secid, state.RowsRead, rowsInsertedReported);
        if (fetchFailure is null && state.SignificantSkippedRows == 0 && state.ReadErrorCount == 0
            && state.MaxAcceptedSourceTime is DateTime maxSourceTime)
        {
            await _cursorWriter.UpsertAsync(
                secid, market, boardId, spec.DataKind, spec.CandleInterval,
                maxSourceTime, lastTradeNo: null, ct);
        }
        if (fetchFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fetchFailure).Throw();
        if (state.SignificantSkippedRows > 0 || state.ReadErrorCount > 0)
            throw new InvalidOperationException("Данные ALGOPACK обработаны частично.");
    }

    private static async Task<long> FlushTradeStatsWritersAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ProtobufBatchWriter<TRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        bool flushStats,
        bool flushCandles,
        CancellationToken ct)
        where TRow : struct
    {
        long statsInserted = 0;
        List<Exception>? failures = null;

        if (flushStats && statsWriter.RowCount > 0)
        {
            try
            {
                statsInserted = await statsWriter.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (flushCandles && candleWriter.RowCount > 0)
        {
            try
            {
                _ = await candleWriter.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (ct.IsCancellationRequested
            && failures is { Count: > 0 }
            && failures[0] is OperationCanceledException)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 })
            throw new AggregateException("Обе вставки TradeStats завершились с ошибкой.", failures);

        return statsInserted;
    }

    private static MoexSeriesSpec? FindSpec(string dataKind, string market)
    {
        for (int i = 0; i < CurrentDaySpecs.Length; i++)
        {
            MoexSeriesSpec spec = CurrentDaySpecs[i];
            if (spec.DataKind == dataKind && spec.Market == market)
                return spec;
        }

        return null;
    }

    private static string GetBoardId(string market)
    {
        if (market == StockMarket)
            return StockBoardId;
        if (market == FuturesMarket)
            return FuturesBoardId;

        throw new InvalidOperationException($"Неизвестный рынок ALGOPACK: '{market}'.");
    }

    private sealed class PeriodicKind
    {
        public PeriodicKind(string dataKind, TimeSpan interval)
        {
            DataKind = dataKind;
            Interval = interval;
        }

        public string DataKind { get; }
        public TimeSpan Interval { get; }
        public bool HasAttempted { get; set; }
        public long LastAttemptTimestamp { get; set; }
    }

    private readonly record struct Hi2InstrumentKey(string Secid, string Market);

}

internal static partial class AlgopackCurrentDayLogMessages
{
    [LoggerMessage(
        EventId = 490,
        EventName = "MoexAlgopackCurrentDayStarted",
        Level = LogLevel.Information,
        Message = "MOEX ALGOPACK current-day service started: turnInterval={TurnInterval}.")]
    public static partial void Started(ILogger logger, TimeSpan turnInterval);

    [LoggerMessage(
        EventId = 491,
        EventName = "MoexAlgopackCurrentDayStopped",
        Level = LogLevel.Information,
        Message = "MOEX ALGOPACK current-day service stopped.")]
    public static partial void Stopped(ILogger logger);

    [LoggerMessage(
        EventId = 492,
        EventName = "MoexAlgopackCurrentDayTurnFailed",
        Level = LogLevel.Error,
        Message = "MOEX ALGOPACK current-day turn failed.")]
    public static partial void TurnFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 493,
        EventName = "MoexAlgopackCurrentDayDataKindFailed",
        Level = LogLevel.Warning,
        Message = "MOEX ALGOPACK current-day data kind failed: dataKind={DataKind}.")]
    public static partial void DataKindFailed(
        ILogger logger, Exception exception, string dataKind);

    [LoggerMessage(
        EventId = 494,
        EventName = "MoexAlgopackCurrentDayInstrumentFailed",
        Level = LogLevel.Warning,
        Message = "MOEX ALGOPACK current-day instrument failed: dataKind={DataKind}, secid={Secid}, market={Market}.")]
    public static partial void InstrumentFailed(
        ILogger logger,
        Exception exception,
        string dataKind,
        string secid,
        string market);

    [LoggerMessage(
        EventId = 495,
        EventName = "MoexAlgopackCurrentDayInstrumentFetchTimedOut",
        Level = LogLevel.Warning,
        Message = "MOEX ALGOPACK current-day fetch timed out: secid={Secid}, market={Market}, dataKind={DataKind}, timeout={Timeout}.")]
    public static partial void InstrumentFetchTimedOut(
        ILogger logger,
        string secid,
        string market,
        string dataKind,
        TimeSpan timeout);
}
