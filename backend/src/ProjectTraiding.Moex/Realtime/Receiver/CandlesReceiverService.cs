using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Realtime.Series;
using ProjectTraiding.Moex.Realtime.Subscriptions;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using Wolverine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём свечей четырёх интервалов по всем подписанным инструментам.
    /// Свечи пишутся с приоритетом приёма 0 (историческая загрузка перекрывает их при слиянии);
    /// Формирующиеся периоды записываются повторно по мере обновления источника. Курсора у свечей
    /// в базе нет, поэтому более старые строки отсекает отдельная граница для каждого интервала.
    /// Желаемый список подписок сверяется на каждом обороте: снятые инструменты исключаются
    /// из опроса немедленно, без перезапуска процесса —
    /// так же, как у сделок и стакана.
    /// </summary>
    public sealed class CandlesReceiverService : RealtimeReceiverServiceBase<CandleInstrumentState>
    {
        protected override string DataKind => "candles";

        protected override TimeSpan StalePollThreshold => _stalePollThreshold;

        private static readonly int[] CandleIntervals = [1, 10, 60, 24];
        private const int CandlesPageLimit = 500;

        // Формат строки приёма задан CandleRow и проверенной схемой moex_candles_*.
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CandleSubscriptionRegistry _candleSubscriptions;
        private readonly ILogger<CandlesReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _stalePollThreshold;
        private readonly int _batchSize;
        private readonly int _maxPagesPerLoad;
        private bool _initialized;

        public CandlesReceiverService(
            IServiceScopeFactory scopeFactory,
            CandleSubscriptionRegistry candleSubscriptions,
            ILogger<CandlesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout,
            TimeSpan stalePollThreshold,
            int batchSize,
            int maxPagesPerLoad)
            : base(pollInterval)
        {
            _scopeFactory = scopeFactory;
            _candleSubscriptions = candleSubscriptions;
            _logger = logger;
            _instrumentFetchTimeout = instrumentFetchTimeout;
            _stalePollThreshold = stalePollThreshold;
            _batchSize = batchSize > 0
                ? batchSize
                : throw new ArgumentOutOfRangeException(nameof(batchSize));
            _maxPagesPerLoad = maxPagesPerLoad > 0
                ? maxPagesPerLoad
                : throw new ArgumentOutOfRangeException(nameof(maxPagesPerLoad));
        }

        protected override void LogStarted(TimeSpan pollInterval)
            => MoexRealtimeReceiverLogMessages.CandlesStarted(_logger, pollInterval);

        protected override void LogTurnFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.CandlesTurnFailed(_logger, exception);

        protected override void LogStopped()
            => MoexRealtimeReceiverLogMessages.CandlesStopped(_logger);

        protected override TimeSpan GetDelayAfterTurn(TimeSpan turnElapsed, TimeSpan pollInterval)
        {
            if (turnElapsed > pollInterval)
            {
                MoexRealtimeReceiverLogMessages.CandlesTurnOverrun(
                    _logger, turnElapsed, pollInterval);
                return TimeSpan.Zero;
            }

            return pollInterval - turnElapsed;
        }

        protected override async Task RunTurnAsync(CancellationToken ct)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

                MoexReceiverInstrumentReader reader =
                    scope.ServiceProvider.GetRequiredService<MoexReceiverInstrumentReader>();

                IReadOnlyList<ReceiverInstrument> instruments =
                    await reader.GetEnabledForDataKindAsync(DataKind, ct);

                if (!_initialized)
                {
                    if (instruments.Count == 0)
                        MoexRealtimeReceiverLogMessages.CandlesSubscriptionsEmpty(_logger);

                    await PrepareInitialInstrumentsAsync(
                        scope.ServiceProvider, instruments, ct);
                    _initialized = true;
                }
                else
                {
                    await ReconcileStatesAsync(
                        scope.ServiceProvider, instruments, ct);
                }

                MoexRealtimeRestClient client =
                    scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();
                ClickHouseInsertExecutor executor =
                    scope.ServiceProvider.GetRequiredService<ClickHouseInsertExecutor>();
                IMessageBus? messageBus = null;
                foreach (KeyValuePair<string, CandleInstrumentState> pair in States)
                {
                    try
                    {
                        messageBus = await PollInstrumentAsync(
                            pair.Key, pair.Value, client, executor,
                            scope.ServiceProvider, messageBus, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        MoexRealtimeReceiverLogMessages.CandlesInstrumentPollFailed(
                            _logger, ex, pair.Key, pair.Value.Market);
                    }
                }
            }
            finally
            {
                // Агрегат публикуется после любого исхода оборота. Иначе при отказе
                // чтения подписок или согласования состояний числа активных и отставших
                // инструментов застынут на прежних значениях, а разрезы снятых подписок
                // не удалятся — до следующего полностью успешного оборота.
                try
                {
                    PublishTelemetrySnapshots();
                }
                catch
                {
                    // Сбой публикации телеметрии не меняет исход оборота.
                }
            }
        }

        protected override Task OpenStateAsync(
            IServiceProvider services,
            ReceiverInstrument instrument,
            string boardId,
            CancellationToken ct)
        {
            States.Add(
                instrument.Secid,
                new CandleInstrumentState(
                    instrument.Market, boardId));
            MoexRealtimeReceiverLogMessages.CandlesInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market);
            return Task.CompletedTask;
        }

        private async Task<IMessageBus?> PollInstrumentAsync(
            string secid,
            CandleInstrumentState state,
            MoexRealtimeRestClient client,
            ClickHouseInsertExecutor executor,
            IServiceProvider scopedServices,
            IMessageBus? messageBus,
            CancellationToken commitCt)
        {
            foreach (int interval in CandleIntervals)
            {
                try
                {
                    messageBus = await PollIntervalAsync(
                        secid, state, client, executor, interval,
                        scopedServices, messageBus, commitCt);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MoexRealtimeReceiverLogMessages.CandlesIntervalPollFailed(
                        _logger, ex, secid, state.Market, interval);
                }
            }

            return messageBus;
        }

        private async Task<IMessageBus?> PollIntervalAsync(
            string secid,
            CandleInstrumentState state,
            MoexRealtimeRestClient client,
            ClickHouseInsertExecutor executor,
            int interval,
            IServiceProvider scopedServices,
            IMessageBus? messageBus,
            CancellationToken commitCt)
        {
            DateTime now = MoexTime.Now;
            bool hasLastBegin = state.LastBeginByInterval.TryGetValue(
                interval, out DateTime lastBegin);
            DateTime? lastBeginForWindow = hasLastBegin ? lastBegin : null;
            DateTime from = RequestWindowStart(interval, now, lastBeginForWindow);
            Dictionary<string, string> queryParams =
                MoexRealtimeRestClient.CreateCandlesQueryParams(from, now, interval);
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeCandlesPoll,
                MoexDataKinds.Candles,
                state.Market,
                MoexFlows.Realtime);
            StorageInsertContext insertContext = new StorageInsertContext(
                operationTags.DataKind,
                operationTags.Market,
                MoexFlows.Realtime,
                CandleInterval: interval);
            ProtobufBatchWriter<CandleRow> writer = new(
                executor,
                MoexSeriesRegistry.CandleTableFor(interval),
                CandleRow.InsertColumns,
                insertContext);

            TimeSpan fetchBudgetRemaining = _instrumentFetchTimeout;
            TimeSpan fetchElapsed = TimeSpan.Zero;
            Exception? fetchFailure = null;
            bool fetchTimedOut = false;
            bool fetchOutcomeRecorded = false;
            int sourceRowsCount = 0;
            int rowFailuresCount = 0;
            long rowsInserted = 0;
            DateTime? maxAcceptedBegin = null;
            DateTime? pendingMaxBegin = null;
            int start = 0;
            int pageNumber = 0;

            using (Activity? pollActivity =
                   MoexTelemetry.ActivitySource.StartActivity("moex.realtime.instrument.poll"))
            {
                pollActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
                pollActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
                pollActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);
                pollActivity?.SetTag(MoexTelemetryAttributes.CandleInterval, interval);

                try
                {
                    using (CancellationTokenSource fetchCts =
                           CancellationTokenSource.CreateLinkedTokenSource(commitCt))
                    {
                        while (true)
                        {
                            commitCt.ThrowIfCancellationRequested();
                            if (fetchBudgetRemaining <= TimeSpan.Zero || fetchCts.IsCancellationRequested)
                            {
                                fetchTimedOut = true;
                                break;
                            }

                            fetchCts.CancelAfter(fetchBudgetRemaining);
                            Stopwatch fetchTimer = Stopwatch.StartNew();
                            bool pageParsed = false;
                            int pageSourceRowsCount = 0;
                            int pageRowFailuresCount = 0;
                            DateTime? pageMaxAcceptedBegin = null;
                            List<CandleRow>? acceptedCandles = _candleSubscriptions.HasSubscribers
                                ? new List<CandleRow>()
                                : null;
                            using (Activity? fetchActivity =
                                   MoexTelemetry.ActivitySource.StartActivity("moex.realtime.fetch"))
                            {
                                fetchActivity?.SetTag(MoexTelemetryAttributes.Source, operationTags.Source);
                                fetchActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
                                fetchActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
                                fetchActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);
                                fetchActivity?.SetTag(MoexTelemetryAttributes.CandleInterval, interval);
                                try
                                {
                                    if (start > 0)
                                        queryParams["start"] = start.ToString(CultureInfo.InvariantCulture);

                                    using (RentedBuffer body = await client.GetCandlesPageAsync(
                                               state.Market,
                                               secid,
                                               queryParams,
                                               fetchCts.Token))
                                    {
                                        MoexRealtimeParser.ParseCandles(
                                            body.Span,
                                            secid,
                                            writer,
                                            now,
                                            lastBeginForWindow,
                                            interval,
                                            start,
                                            _logger,
                                            acceptedCandles,
                                            out pageSourceRowsCount,
                                            out _,
                                            out pageRowFailuresCount,
                                            out pageMaxAcceptedBegin);
                                    }
                                    pageParsed = true;
                                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                                }
                                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                                {
                                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                                    throw;
                                }
                                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                                {
                                    fetchTimedOut = true;
                                    fetchActivity?.SetStatus(ActivityStatusCode.Error);
                                }
                                catch (Exception ex)
                                {
                                    fetchFailure = ex;
                                    if (ex is MoexSchemaMismatchException)
                                    {
                                        MoexLogMessages.ParseFailed(
                                            _logger, ex, BuildCandlesEndpoint(state.Market, secid),
                                            MoexErrorTypes.SchemaMismatch, ex.Message);
                                    }
                                    fetchActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                                }
                                finally
                                {
                                    fetchTimer.Stop();
                                    fetchElapsed += fetchTimer.Elapsed;
                                    fetchBudgetRemaining -= fetchTimer.Elapsed;
                                    if (!fetchCts.IsCancellationRequested)
                                        fetchCts.CancelAfter(Timeout.InfiniteTimeSpan);
                                }
                            }

                            if (!pageParsed)
                                break;

                            sourceRowsCount += pageSourceRowsCount;
                            rowFailuresCount += pageRowFailuresCount;
                            if (acceptedCandles is not null && acceptedCandles.Count > 0)
                            {
                                try
                                {
                                    messageBus ??= scopedServices.GetRequiredService<IMessageBus>();
                                }
                                catch (Exception ex)
                                {
                                    CandleRow firstCandle = acceptedCandles[0];
                                    MoexRealtimeReceiverLogMessages.CandleAcceptedPublishFailed(
                                        _logger, ex, firstCandle.Secid, state.Market,
                                        interval, firstCandle.Begin);
                                }

                                if (messageBus is not null)
                                {
                                    for (int i = 0; i < acceptedCandles.Count; i++)
                                    {
                                        commitCt.ThrowIfCancellationRequested();
                                        CandleRow candle = acceptedCandles[i];
                                        try
                                        {
                                            await messageBus.PublishAsync(
                                                new CandleAccepted(candle, state.Market, interval));
                                            commitCt.ThrowIfCancellationRequested();
                                        }
                                        catch (OperationCanceledException)
                                            when (commitCt.IsCancellationRequested)
                                        {
                                            throw;
                                        }
                                        catch (Exception ex)
                                        {
                                            MoexRealtimeReceiverLogMessages.CandleAcceptedPublishFailed(
                                                _logger, ex, candle.Secid, state.Market,
                                                interval, candle.Begin);
                                        }
                                    }
                                }
                            }

                            if (pageMaxAcceptedBegin is DateTime pageMaximum)
                            {
                                if (maxAcceptedBegin is null || pageMaximum > maxAcceptedBegin.Value)
                                    maxAcceptedBegin = pageMaximum;
                                if (pendingMaxBegin is null || pageMaximum > pendingMaxBegin.Value)
                                    pendingMaxBegin = pageMaximum;
                            }

                            if (writer.RowCount >= _batchSize)
                            {
                                int expectedRows = writer.RowCount;
                                long insertedRows = await writer.FlushAsync(commitCt);
                                if (insertedRows != expectedRows)
                                {
                                    throw new InvalidOperationException(
                                        $"ClickHouse подтвердил запись {insertedRows} из {expectedRows} свечей " +
                                        $"интервала {interval} для {secid}.");
                                }
                                state.RowsTotal += insertedRows;
                                rowsInserted += insertedRows;
                                UpdateConfirmedTime(state, interval, pendingMaxBegin);
                                pendingMaxBegin = null;
                            }

                            pageNumber++;
                            commitCt.ThrowIfCancellationRequested();
                            bool fetchBudgetExpired = fetchBudgetRemaining <= TimeSpan.Zero ||
                                                       fetchCts.IsCancellationRequested;
                            if (pageSourceRowsCount != CandlesPageLimit)
                            {
                                if (fetchBudgetExpired)
                                    fetchTimedOut = true;
                                break;
                            }
                            if (fetchBudgetExpired)
                            {
                                fetchTimedOut = true;
                                break;
                            }
                            if (pageNumber >= _maxPagesPerLoad)
                            {
                                fetchFailure = new InvalidOperationException(
                                    $"Догрузка свечей достигла защитного предела " +
                                    $"Moex:MaxPagesPerLoad={_maxPagesPerLoad} на полной странице.");
                                break;
                            }
                            start = checked(start + CandlesPageLimit);
                        }
                    }

                    if (fetchTimedOut)
                    {
                        MoexRealtimeReceiverLogMessages.CandlesInstrumentFetchTimedOut(
                            _logger, secid, state.Market, interval, _instrumentFetchTimeout);
                        MoexMetrics.RecordOperationTimeout(
                            in operationTags, fetchElapsed.TotalSeconds);
                        fetchOutcomeRecorded = true;
                    }
                    else if (fetchFailure is not null)
                    {
                        MoexMetrics.RecordOperationError(
                            in operationTags, fetchFailure, fetchElapsed.TotalSeconds);
                        fetchOutcomeRecorded = true;
                    }
                    else
                    {
                        TagList rowsTags = new TagList
                        {
                            { MoexTelemetryAttributes.Source, operationTags.Source },
                            { MoexTelemetryAttributes.DataKind, operationTags.DataKind },
                            { MoexTelemetryAttributes.Market, operationTags.Market },
                            { MoexTelemetryAttributes.Flow, operationTags.Flow },
                            { MoexTelemetryAttributes.CandleInterval, interval },
                        };
                        MoexMetrics.RowsReceived.Add(sourceRowsCount, rowsTags);
                        MoexMetrics.RecordOperationSuccess(
                            in operationTags, fetchElapsed.TotalSeconds);
                        fetchOutcomeRecorded = true;
                    }

                    if (fetchFailure is null && !fetchTimedOut && writer.RowCount > 0)
                    {
                        int expectedRows = writer.RowCount;
                        long insertedRows = await writer.FlushAsync(commitCt);
                        if (insertedRows != expectedRows)
                        {
                            throw new InvalidOperationException(
                                $"ClickHouse подтвердил запись {insertedRows} из {expectedRows} свечей " +
                                $"интервала {interval} для {secid}.");
                        }
                        state.RowsTotal += insertedRows;
                        rowsInserted += insertedRows;
                        UpdateConfirmedTime(state, interval, pendingMaxBegin);
                    }

                    if (fetchTimedOut)
                    {
                        MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error, interval);
                        pollActivity?.SetStatus(ActivityStatusCode.Error);
                        return messageBus;
                    }
                    if (fetchFailure is not null)
                    {
                        pollActivity?.SetStatus(ActivityStatusCode.Error, fetchFailure.Message);
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo
                            .Capture(fetchFailure)
                            .Throw();
                    }
                    if (rowFailuresCount > 0)
                        throw new InvalidOperationException("Окно свечей обработано частично.");

                    if (rowsInserted > 0 && maxAcceptedBegin is DateTime confirmedBegin)
                        state.LastBeginByInterval[interval] = confirmedBegin;

                    DateTimeOffset pollSucceededAt = DateTimeOffset.UtcNow;
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Success, interval);
                    state.LastSuccessfulPollByInterval[interval] = pollSucceededAt;
                    state.LastSuccessfulPollTime = pollSucceededAt;
                    pollActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    pollActivity?.SetStatus(ActivityStatusCode.Ok);
                    if (!fetchOutcomeRecorded)
                    {
                        MoexMetrics.RecordOperationCancelled(
                            in operationTags, fetchElapsed.TotalSeconds);
                    }
                    MoexMetrics.RecordRealtimePoll(
                        in operationTags, MoexOutcomes.Cancelled, interval);
                    throw;
                }
                catch (Exception)
                {
                    pollActivity?.SetStatus(ActivityStatusCode.Error);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error, interval);
                    throw;
                }
            }

            return messageBus;
        }

        private static string BuildCandlesEndpoint(string market, string secid)
        {
            if (market == MoexMarkets.Stock)
                return $"/engines/stock/markets/shares/boards/TQBR/securities/{secid}/candles.json";
            return $"/engines/futures/markets/forts/boards/RFUD/securities/{secid}/candles.json";
        }
        private static void UpdateConfirmedTime(
            CandleInstrumentState state,
            int interval,
            DateTime? confirmedBegin)
        {
            if (confirmedBegin is not DateTime begin)
                return;

            DateTimeOffset confirmedTime = MoexTime.ToDateTimeOffset(begin);
            state.LastConfirmedMarketTimeByInterval[interval] = confirmedTime;
            if (state.LastConfirmedMarketTime is null ||
                confirmedTime > state.LastConfirmedMarketTime.Value)
            {
                state.LastConfirmedMarketTime = confirmedTime;
            }
        }

        private static DateTime RequestWindowStart(int interval, DateTime now, DateTime? lastBegin)
        {
            return interval switch
            {
                1 => now.AddMinutes(-3),
                10 => now.AddMinutes(-30),
                60 => now.AddHours(-3),
                // Курсор уменьшает каждое последующее окно до актуального периода. Холодный старт
                // и длительные выходные сохраняют запас в 14 календарных дней.
                24 => lastBegin is not null && lastBegin.Value > now.Date.AddDays(-14)
                    ? lastBegin.Value
                    : now.Date.AddDays(-14),
                _ => throw new InvalidOperationException(
                    $"Неизвестный интервал свечей приёма: {interval}."),
            };
        }

        protected override void LogInstrumentPreparationFailed(Exception exception, string secid)
            => MoexRealtimeReceiverLogMessages.CandlesInstrumentPreparationFailed(
                _logger, exception, secid);

        protected override void LogInstrumentStopped(string secid, long rowsTotal)
            => MoexRealtimeReceiverLogMessages.CandlesInstrumentStopped(
                _logger, secid, rowsTotal);

        protected override void AddTelemetrySnapshotsForMarket(
            string market,
            DateTimeOffset now,
            List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> snapshots)
        {
            base.AddTelemetrySnapshotsForMarket(market, now, snapshots);

            long activeInstruments = 0;
            foreach (KeyValuePair<string, CandleInstrumentState> pair in States)
            {
                if (pair.Value.Market == market)
                    activeInstruments++;
            }

            if (activeInstruments == 0)
                return;

            foreach (int interval in CandleIntervals)
            {
                DateTimeOffset? lastPoll = null;
                DateTimeOffset? lastConfirmed = null;
                foreach (KeyValuePair<string, CandleInstrumentState> pair in States)
                {
                    CandleInstrumentState state = pair.Value;
                    if (state.Market != market)
                        continue;

                    if (state.LastSuccessfulPollByInterval.TryGetValue(
                            interval, out DateTimeOffset successfulPoll) &&
                        (lastPoll is null || successfulPoll > lastPoll.Value))
                    {
                        lastPoll = successfulPoll;
                    }

                    if (state.LastConfirmedMarketTimeByInterval.TryGetValue(
                            interval, out DateTimeOffset confirmedMarketTime) &&
                        (lastConfirmed is null || confirmedMarketTime > lastConfirmed.Value))
                    {
                        lastConfirmed = confirmedMarketTime;
                    }
                }

                snapshots.Add(new KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>(
                    new RealtimeTelemetryKey(DataKind, market, interval),
                    new RealtimeTelemetrySnapshot(lastPoll, lastConfirmed, 0, 0)));
            }
        }

    }

    public sealed class CandleInstrumentState : ReceiverInstrumentState
    {
        public CandleInstrumentState(
            string market, string boardId)
            : base(market, boardId)
        {
        }

        /// <summary>
        /// Наибольшее начало свечи, успешно записанной по каждому интервалу.
        /// Границы не должны пересекаться между минутами, часами и торговыми днями.
        /// </summary>
        public Dictionary<int, DateTime> LastBeginByInterval { get; } = new();

        /// <summary>Последний успешный опрос каждого интервала; пустой ответ тоже обновляет отметку.</summary>
        public Dictionary<int, DateTimeOffset> LastSuccessfulPollByInterval { get; } = new();

        /// <summary>Время последней фактически записанной свечи отдельно для каждого интервала.</summary>
        public Dictionary<int, DateTimeOffset> LastConfirmedMarketTimeByInterval { get; } = new();
    }
}
