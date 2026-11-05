using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Realtime.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём свечей четырёх интервалов по всем подписанным инструментам.
    /// Свечи пишутся с приоритетом приёма 0 (историческая загрузка перекрывает их при слиянии);
    /// незавершённые периоды в ClickHouse не попадают. Курсора у свечей в базе нет, поэтому
    /// повторную запись в пределах запуска отсекает отдельная граница для каждого интервала.
    /// Желаемый список подписок сверяется на каждом обороте: снятые инструменты исключаются
    /// из опроса немедленно, без перезапуска процесса —
    /// так же, как у сделок и стакана.
    /// </summary>
    public sealed class CandlesReceiverService : RealtimeReceiverServiceBase<CandleInstrumentState>
    {
        protected override string DataKind => "candles";

        protected override TimeSpan StalePollThreshold => _stalePollThreshold;

        private static readonly int[] CandleIntervals = [1, 10, 60, 24];

        // Формат строки приёма задан общей декларацией свечей:
        // secid, open, close, high, low, value, volume, begin, end, ingest_priority.
        private const int CandleEndColumnIndex = 8;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CandlesReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _stalePollThreshold;
        private readonly List<object?[]> _closed = new();
        private bool _initialized;

        public CandlesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<CandlesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout,
            TimeSpan stalePollThreshold)
            : base(pollInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _instrumentFetchTimeout = instrumentFetchTimeout;
            _stalePollThreshold = stalePollThreshold;
        }

        protected override void LogStarted(TimeSpan pollInterval)
            => MoexRealtimeReceiverLogMessages.CandlesStarted(_logger, pollInterval);

        protected override void LogTurnFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.CandlesTurnFailed(_logger, exception);

        protected override void LogStopped()
            => MoexRealtimeReceiverLogMessages.CandlesStopped(_logger);

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
                RealtimeSpecRowWriter writer =
                    scope.ServiceProvider.GetRequiredService<RealtimeSpecRowWriter>();
                foreach (KeyValuePair<string, CandleInstrumentState> pair in States)
                {
                    try
                    {
                        await PollInstrumentAsync(
                            pair.Key, pair.Value, client, writer, ct);
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

        private async Task PollInstrumentAsync(
            string secid,
            CandleInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeSpecRowWriter writer,
            CancellationToken commitCt)
        {
            foreach (int interval in CandleIntervals)
            {
                DateTimeOffset pollAttemptTime = DateTimeOffset.UtcNow;
                if (!ShouldPollInterval(state, interval, pollAttemptTime))
                    continue;

                if (interval != 1)
                    state.LastPollAttemptByInterval[interval] = pollAttemptTime;

                try
                {
                    await PollIntervalAsync(
                        secid, state, client, writer, interval, commitCt);
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
        }

        private async Task PollIntervalAsync(
            string secid,
            CandleInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeSpecRowWriter writer,
            int interval,
            CancellationToken commitCt)
        {
            MoexRealtimeSpec spec = MoexRealtimeRegistry.CandlesFor(interval);

            // Московское время фиксируем один раз: им задаётся и окно запроса, и граница «закрыта».
            DateTime now = MoexTime.Now;
            DateTime from = RequestWindowStart(interval, now);

            // Получение окна свечей — под собственным бюджетом, живущим строго вокруг вызова клиента
            // и уничтожаемым до фиксации. Причины те же, что в стакане: таймер бюджета не должен
            // сработать во время записи и выдать сбой хранилища за тайм-аут получения.
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

            RealtimeCandlesPage candles;
            using (Activity? pollActivity =
                   MoexTelemetry.ActivitySource.StartActivity("moex.realtime.instrument.poll"))
            {
            pollActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
            pollActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
            pollActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);
            pollActivity?.SetTag(MoexTelemetryAttributes.CandleInterval, interval);

            long fetchStart = Stopwatch.GetTimestamp();

            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                using Activity? fetchActivity =
                    MoexTelemetry.ActivitySource.StartActivity("moex.realtime.fetch");
                fetchActivity?.SetTag(MoexTelemetryAttributes.Source, operationTags.Source);
                fetchActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
                fetchActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
                fetchActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);
                fetchActivity?.SetTag(MoexTelemetryAttributes.CandleInterval, interval);
                try
                {
                    candles = await client.GetCandlesTodayAsync(
                        state.Market,
                        secid,
                        from,
                        now,
                        interval,
                        fetchCts.Token);

                    TagList rowsTags = new TagList
                    {
                        { MoexTelemetryAttributes.Source, operationTags.Source },
                        { MoexTelemetryAttributes.DataKind, operationTags.DataKind },
                        { MoexTelemetryAttributes.Market, operationTags.Market },
                        { MoexTelemetryAttributes.Flow, operationTags.Flow },
                        { MoexTelemetryAttributes.CandleInterval, interval },
                    };
                    MoexMetrics.RowsReceived.Add(candles.SourceRowsCount, rowsTags);

                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                    pollActivity?.SetStatus(ActivityStatusCode.Ok);
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(
                        in operationTags, MoexOutcomes.Cancelled, interval);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error);
                    pollActivity?.SetStatus(ActivityStatusCode.Error);
                    MoexRealtimeReceiverLogMessages.CandlesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, interval, _instrumentFetchTimeout);
                    MoexMetrics.RecordOperationTimeout(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error, interval);
                    return;
                }
                catch (Exception ex)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    MoexMetrics.RecordOperationError(
                        in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error, interval);
                    throw;
                }
            }

            // Границы пачки и наиболее новое начало считаются одним проходом страницы.
            // Для 10/60 минут окончание периода подтверждают его длительность или более
            // новая свеча; дневной интервал закрывается через 24 часа от begin,
            // а свеча следующей торговой даты остаётся дополнительным подтверждением.
            // Тот же буфер на все опросы: очищается перед отбором. Предыдущая вставка к этому
            // моменту уже завершена — она выполняется с ожиданием внутри того же опроса.
            // Принадлежит одному обороту одной службы; опросы выполняются последовательно.
            _closed.Clear();
            List<object?[]> closed = _closed;
            bool hasLastClosedBegin = state.LastClosedBeginByInterval.TryGetValue(
                interval, out DateTime lastClosedBegin);
            DateTime maxClosedBegin = hasLastClosedBegin ? lastClosedBegin : DateTime.MinValue;
            DateTime? newestBegin = null;

            try
            {
                foreach (RealtimeRowFailure failure in candles.RowFailures)
                    MoexRealtimeReceiverLogMessages.CandleRowSkipped(
                        _logger, secid, DataKind, interval, failure.Position, failure.Reason);

                for (int i = 0; i < candles.Rows.Count; i++)
                {
                    DateTime? begin = candles.Rows[i].Begin;
                    if (begin is not null &&
                        (newestBegin is null || begin.Value > newestBegin.Value))
                    {
                        newestBegin = begin.Value;
                    }
                }

                for (int i = 0; i < candles.Rows.Count; i++)
                {
                    DateTime? begin = candles.Rows[i].Begin;

                    // Свеча без начала пропускается и страницу не отвергает: приём этим
                    // отличается от исторической загрузки намеренно.
                    if (begin is null)
                        continue;

                    DateTime beginValue = begin.Value;
                    DateTime? endValue = candles.Rows[i].Row[CandleEndColumnIndex] is DateTime end
                        ? end
                        : null;
                    if (!IsClosedCandle(interval, beginValue, endValue, newestBegin, now))
                        continue;

                    if (hasLastClosedBegin &&
                        (interval == 24
                            ? beginValue < lastClosedBegin
                            : beginValue <= lastClosedBegin))
                        continue;

                    closed.Add(candles.Rows[i].Row);

                    if (beginValue > maxClosedBegin)
                        maxClosedBegin = beginValue;
                }

                if (closed.Count > 0)
                {
                    // Фиксация — под хостовым commitCt. У свечей курсора в базе нет; durable-запись —
                    // только ClickHouse, за ней сразу двигаются граница и счётчик состояния. Запись
                    // предварительна (приоритет 0): источник истины — исторический загрузчик (приоритет 1),
                    // он перекрывает свечу при слиянии. До планировщика догрузки текущего дня
                    // предварительная версия может дожить до ручного прогона; точные расчёты обязаны
                    // считать строки приёмника предварительными.
                    long insertedRows = await writer.WriteAsync(
                        spec,
                        secid,
                        closed,
                        insertContext,
                        commitCt);
                    state.RowsTotal += insertedRows;
                    if (insertedRows != closed.Count)
                    {
                        throw new InvalidOperationException(
                            $"ClickHouse подтвердил запись {insertedRows} из {closed.Count} свечей " +
                            $"интервала {interval} для {secid}.");
                    }

                    if (candles.RowFailures.Count == 0)
                        state.LastClosedBeginByInterval[interval] = maxClosedBegin;

                    DateTimeOffset confirmedTime = MoexTime.ToDateTimeOffset(maxClosedBegin);
                    state.LastConfirmedMarketTimeByInterval[interval] = confirmedTime;
                    if (state.LastConfirmedMarketTime is null ||
                        confirmedTime > state.LastConfirmedMarketTime.Value)
                    {
                        state.LastConfirmedMarketTime = confirmedTime;
                    }
                }

                if (candles.RowFailures.Count > 0)
                    throw new InvalidOperationException("Окно свечей обработано частично.");

                DateTimeOffset pollSucceededAt = DateTimeOffset.UtcNow;
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Success, interval);
                state.LastSuccessfulPollByInterval[interval] = pollSucceededAt;
                state.LastSuccessfulPollTime = pollSucceededAt;
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
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

        }

        private bool ShouldPollInterval(
            CandleInstrumentState state,
            int interval,
            DateTimeOffset now)
        {
            if (interval == 1)
                return true;

            TimeSpan pollCadence = interval switch
            {
                10 => TimeSpan.FromMinutes(1),
                60 => TimeSpan.FromMinutes(5),
                24 => TimeSpan.FromMinutes(10),
                _ => throw new InvalidOperationException(
                    $"Неизвестный интервал свечей приёма: {interval}."),
            };

            return !state.LastPollAttemptByInterval.TryGetValue(interval, out DateTimeOffset lastPoll)
                || now - lastPoll >= pollCadence;
        }

        private static DateTime RequestWindowStart(int interval, DateTime now)
        {
            return interval switch
            {
                1 => now.AddMinutes(-3),
                10 => now.AddMinutes(-30),
                60 => now.AddHours(-3),
                // Недельный перерыв и длинные выходные не должны скрыть последнюю
                // торговую дату; исторический тракт остаётся источником полной истории.
                24 => now.Date.AddDays(-14),
                _ => throw new InvalidOperationException(
                    $"Неизвестный интервал свечей приёма: {interval}."),
            };
        }

        private static bool IsClosedCandle(
            int interval,
            DateTime begin,
            DateTime? end,
            DateTime? newestBegin,
            DateTime now)
        {
            // Это прежнее правило минутного приёма, оставленное без изменений.
            if (interval == 1)
                return begin.AddMinutes(1) <= now;

            if (interval == 24)
                return begin <= now;

            if (end is not null && end.Value > now)
                return false;

            bool hasNewerCandle = newestBegin is not null
                && newestBegin.Value > begin
                && newestBegin.Value <= now;

            TimeSpan duration = interval switch
            {
                10 => TimeSpan.FromMinutes(10),
                60 => TimeSpan.FromHours(1),
                _ => throw new InvalidOperationException(
                    $"Неизвестный интервал свечей приёма: {interval}."),
            };

            // end может содержать время последней сделки внутри свечи, поэтому не служит
            // доказательством закрытия периода.
            TimeSpan sourceDelayGrace = interval == 10
                ? TimeSpan.FromMinutes(1)
                : TimeSpan.FromMinutes(2);
            return begin + duration + sourceDelayGrace <= now || hasNewerCandle;
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
        /// Начало последней записанной закрытой свечи отдельно по каждому интервалу.
        /// Границы не должны пересекаться между минутами, часами и торговыми днями.
        /// </summary>
        public Dictionary<int, DateTime> LastClosedBeginByInterval { get; } = new();

        /// <summary>Время последней попытки старшего интервала для ограничения частоты опроса.</summary>
        public Dictionary<int, DateTimeOffset> LastPollAttemptByInterval { get; } = new();

        /// <summary>Последний успешный опрос каждого интервала; пустой ответ тоже обновляет отметку.</summary>
        public Dictionary<int, DateTimeOffset> LastSuccessfulPollByInterval { get; } = new();

        /// <summary>Время последней фактически записанной свечи отдельно для каждого интервала.</summary>
        public Dictionary<int, DateTimeOffset> LastConfirmedMarketTimeByInterval { get; } = new();
    }
}
