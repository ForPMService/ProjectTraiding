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
    /// Периодический приём свечей текущего торгового дня по всем подписанным инструментам.
    /// Закрытые минуты пишутся в moex_candles_1m с приоритетом приёма 0 (историческая загрузка
    /// перекрывает при слиянии); текущая незакрытая минута в ClickHouse не пишется, она ещё
    /// меняется. Курсора у свечей в базе нет; повтор записи
    /// закрытых минут в пределах запуска отсекает граница LastClosedBegin в состоянии.
    /// Желаемый список подписок сверяется на каждом обороте: снятые инструменты исключаются
    /// из опроса немедленно, а их сеансы покрытия закрываются штатно без перезапуска процесса —
    /// так же, как у сделок и стакана.
    /// </summary>
    public sealed class CandlesReceiverService : RealtimeReceiverServiceBase<CandleInstrumentState>
    {
        protected override string DataKind => "candles";

        protected override TimeSpan StalePollThreshold => _stalePollThreshold;

        protected override int? StorageCandleInterval => CandleInterval;

        // Реальное время пока только минутная свеча. Контракт подписок (V026) держит это CHECK-ом.
        private const int CandleInterval = 1;

        // Хвост окна опроса, минуты. Одна-две минуты укладываются в одну страницу клиента; запас
        // в несколько минут страхует от пропущенного оборота и остаётся в пределах одной страницы.
        // Историю торгового дня добирает исторический загрузчик, здесь только текущий хвост.
        private const int WindowMinutes = 3;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CandlesReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _heartbeatMinInterval;
        private readonly TimeSpan _stalePollThreshold;
        private readonly List<object?[]> _closed = new();
        private bool _initialized;

        public CandlesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<CandlesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout,
            TimeSpan heartbeatMinInterval,
            TimeSpan stalePollThreshold)
            : base(pollInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _instrumentFetchTimeout = instrumentFetchTimeout;
            _heartbeatMinInterval = heartbeatMinInterval;
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
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                IReadOnlyList<ReceiverInstrument> instruments =
                    await reader.GetEnabledForDataKindAsync(DataKind, ct);

                if (!_initialized)
                {
                    if (instruments.Count == 0)
                        MoexRealtimeReceiverLogMessages.CandlesSubscriptionsEmpty(_logger);

                    await PrepareInitialInstrumentsAsync(
                        scope.ServiceProvider, instruments, coverageWriter, ct);
                    _initialized = true;
                }
                else
                {
                    await ReconcileStatesAsync(
                        scope.ServiceProvider, instruments, coverageWriter, ct);
                }

                MoexRealtimeRestClient client =
                    scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();
                RealtimeSpecRowWriter writer =
                    scope.ServiceProvider.GetRequiredService<RealtimeSpecRowWriter>();
                foreach (KeyValuePair<string, CandleInstrumentState> pair in States)
                {
                    if (pair.Value.IsStopping)
                        continue;

                    try
                    {
                        await PollInstrumentAsync(
                            pair.Key, pair.Value, client, writer, coverageWriter, ct);
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

        protected override async Task OpenStateAsync(
            IServiceProvider services,
            ReceiverInstrument instrument,
            string boardId,
            CancellationToken ct)
        {
            StreamCoverageWriter coverageWriter =
                services.GetRequiredService<StreamCoverageWriter>();
            MoexDataGenerationReader generationReader =
                services.GetRequiredService<MoexDataGenerationReader>();

            // Чтение идёт перед открытием сеанса: отказ здесь не оставляет открытой строки
            // покрытия, за которой никто не следит.
            string dataGeneration = await generationReader.GetAsync(instrument.Secid, ct);

            long sessionId = await coverageWriter.OpenSessionAsync(
                instrument.Secid, instrument.Market, boardId, DataKind, CandleInterval, ct);
            long heartbeatTimestamp = Stopwatch.GetTimestamp();
            States.Add(
                instrument.Secid,
                new CandleInstrumentState(
                    sessionId, instrument.Market, boardId, heartbeatTimestamp)
                {
                    DataGeneration = dataGeneration,
                });
            MoexRealtimeReceiverLogMessages.CandlesInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private async Task PollInstrumentAsync(
            string secid,
            CandleInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeSpecRowWriter writer,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            MoexRealtimeSpec spec = MoexRealtimeRegistry.CandlesFor(CandleInterval);

            // Московское время фиксируем один раз: им задаётся и окно запроса, и граница «закрыта».
            DateTime now = MoexTime.Now;
            DateTime from = now.AddMinutes(-WindowMinutes);

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
                MoexFlows.Realtime);

            List<(object?[] Row, DateTime? Begin)> candles;
            using (Activity? pollActivity =
                   MoexTelemetry.ActivitySource.StartActivity("moex.realtime.instrument.poll"))
            {
            pollActivity?.SetTag(MoexTelemetryAttributes.DataKind, operationTags.DataKind);
            pollActivity?.SetTag(MoexTelemetryAttributes.Market, operationTags.Market);
            pollActivity?.SetTag(MoexTelemetryAttributes.Secid, secid);

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
                try
                {
                    candles = await client.GetCandlesTodayAsync(
                        state.Market,
                        secid,
                        from,
                        now,
                        CandleInterval,
                        fetchCts.Token);

                    TagList rowsTags = new TagList
                    {
                        { MoexTelemetryAttributes.Source, operationTags.Source },
                        { MoexTelemetryAttributes.DataKind, operationTags.DataKind },
                        { MoexTelemetryAttributes.Market, operationTags.Market },
                        { MoexTelemetryAttributes.Flow, operationTags.Flow },
                    };
                    MoexMetrics.RowsReceived.Add(candles.Count, rowsTags);

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
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error);
                    pollActivity?.SetStatus(ActivityStatusCode.Error);
                    MoexRealtimeReceiverLogMessages.CandlesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    MoexMetrics.RecordOperationTimeout(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                    return;
                }
                catch (Exception ex)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    MoexMetrics.RecordOperationError(
                        in operationTags, ex, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                    throw;
                }
            }

            // Минута с началом B закрыта, когда московское время достигло B + интервал.
            // Границы отобранной пачки считаются этим же проходом: отдельный проход
            // по отобранным строкам не нужен.
            // Тот же буфер на все опросы: очищается перед отбором. Предыдущая вставка к этому
            // моменту уже завершена — она выполняется с ожиданием внутри того же опроса.
            // Принадлежит одному обороту одной службы, см. предупреждение о пункте 8 плана.
            _closed.Clear();
            List<object?[]> closed = _closed;
            DateTime maxClosedBegin = state.LastClosedBegin ?? DateTime.MinValue;
            DateTime firstClosedBegin = default;
            DateTime lastClosedBegin = default;

            try
            {
                for (int i = 0; i < candles.Count; i++)
                {
                    DateTime? begin = candles[i].Begin;

                    // Свеча без начала пропускается и страницу не отвергает: приём этим
                    // отличается от исторической загрузки намеренно.
                    if (begin is null)
                        continue;

                    DateTime beginValue = begin.Value;
                    if (beginValue.AddMinutes(CandleInterval) > now)
                        continue;

                    if (state.LastClosedBegin is not null &&
                        beginValue <= state.LastClosedBegin.Value)
                        continue;

                    if (closed.Count == 0)
                        firstClosedBegin = beginValue;

                    lastClosedBegin = beginValue;
                    closed.Add(candles[i].Row);

                    if (beginValue > maxClosedBegin)
                        maxClosedBegin = beginValue;
                }

                if (closed.Count > 0)
                {
                    // Фиксация — под хостовым commitCt. У свечей курсора в базе нет; durable-запись —
                    // только ClickHouse, за ней сразу двигаются граница и счётчик состояния. Запись
                    // предварительна (приоритет 0): источник истины — исторический загрузчик (приоритет 1),
                    // он перекрывает минуту при слиянии. До планировщика догрузки текущего дня
                    // предварительная версия может дожить до ручного прогона; точные расчёты обязаны
                    // считать строки приёмника предварительными.
                    await writer.WriteAsync(
                        spec,
                        secid,
                        state.DataGeneration,
                        closed,
                        firstClosedBegin,
                        lastClosedBegin,
                        insertContext,
                        commitCt);
                    state.RowsTotal += closed.Count;
                    state.LastClosedBegin = maxClosedBegin;
                    state.LastConfirmedMarketTime = MoexTime.ToDateTimeOffset(maxClosedBegin);
                }

                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Success);
                state.LastSuccessfulPollTime = DateTimeOffset.UtcNow;
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Ok);
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
                throw;
            }
            catch (Exception)
            {
                pollActivity?.SetStatus(ActivityStatusCode.Error);
                MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                throw;
            }
            }

            await ReceiverSessionHeartbeat.WriteIfDueAsync(
                state, _heartbeatMinInterval, coverageWriter, commitCt);
        }

        protected override IServiceScopeFactory ScopeFactory => _scopeFactory;

        protected override void LogSessionCloseFailed(Exception exception, string secid, long sessionId)
            => MoexRealtimeReceiverLogMessages.CandlesSessionCloseFailed(
                _logger, exception, secid, sessionId);

        protected override void LogShutdownFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.CandlesShutdownFailed(_logger, exception);

        protected override void LogInstrumentPreparationFailed(Exception exception, string secid)
            => MoexRealtimeReceiverLogMessages.CandlesInstrumentPreparationFailed(
                _logger, exception, secid);

        protected override void LogInstrumentStopping(string secid, long sessionId)
            => MoexRealtimeReceiverLogMessages.CandlesInstrumentStopping(_logger, secid, sessionId);

        protected override void LogInstrumentStopped(string secid, long sessionId, long rowsTotal)
            => MoexRealtimeReceiverLogMessages.CandlesInstrumentStopped(
                _logger, secid, sessionId, rowsTotal);

    }

    public sealed class CandleInstrumentState : ReceiverInstrumentSessionState
    {
        public CandleInstrumentState(
            long sessionId, string market, string boardId, long lastHeartbeatTimestamp)
            : base(sessionId, market, boardId, lastHeartbeatTimestamp)
        {
        }

        /// <summary>
        /// Начало последней записанной закрытой минуты. Хранится только в пределах
        /// текущего запуска и отсекает повторную запись уже обработанных минут.
        /// </summary>
        public DateTime? LastClosedBegin { get; set; }
    }
}
