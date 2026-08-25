using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Realtime.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём полных снимков стакана по всем инструментам. Стакан ведёт только
    /// покрытие: курсора у полного снимка нет и в moex_stream_cursors он не записывается.
    /// </summary>
    public sealed class OrderbookReceiverService : RealtimeReceiverServiceBase<ReceiverInstrumentSessionState>
    {
        protected override string DataKind => "orderbook";

        protected override TimeSpan StalePollThreshold => _stalePollThreshold;

        protected override int? StorageCandleInterval => null;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderbookReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _heartbeatMinInterval;
        private readonly TimeSpan _stalePollThreshold;
        private bool _initialized;

        public OrderbookReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<OrderbookReceiverService> logger,
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
            => MoexRealtimeReceiverLogMessages.OrderbookStarted(_logger, pollInterval);

        protected override void LogTurnFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.OrderbookTurnFailed(_logger, exception);

        protected override void LogStopped()
            => MoexRealtimeReceiverLogMessages.OrderbookStopped(_logger);

        protected override async Task RunTurnAsync(CancellationToken ct)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

                MoexReceiverInstrumentReader instrumentReader =
                    scope.ServiceProvider.GetRequiredService<MoexReceiverInstrumentReader>();
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                IReadOnlyList<ReceiverInstrument> instruments =
                    await instrumentReader.GetEnabledForDataKindAsync(DataKind, ct);
                if (!_initialized)
                {
                    if (instruments.Count == 0)
                        MoexRealtimeReceiverLogMessages.OrderbookSubscriptionsEmpty(_logger);

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
                foreach (KeyValuePair<string, ReceiverInstrumentSessionState> pair in States)
                {
                    if (pair.Value.IsStopping)
                        continue;

                    try
                    {
                        await PollInstrumentAsync(
                            pair.Key,
                            pair.Value,
                            client,
                            writer,
                            coverageWriter,
                            ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        MoexRealtimeReceiverLogMessages.OrderbookInstrumentPollFailed(
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
            long sessionId = await coverageWriter.OpenSessionAsync(
                instrument.Secid, instrument.Market, boardId, DataKind, null, ct);
            long heartbeatTimestamp = Stopwatch.GetTimestamp();
            States.Add(
                instrument.Secid,
                new ReceiverInstrumentSessionState(
                    sessionId, instrument.Market, boardId, heartbeatTimestamp));
            MoexRealtimeReceiverLogMessages.OrderbookInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private async Task PollInstrumentAsync(
            string secid,
            ReceiverInstrumentSessionState state,
            MoexRealtimeRestClient client,
            RealtimeSpecRowWriter writer,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            MoexRealtimeSpec spec = MoexRealtimeRegistry.Orderbook;

            // Снимок стакана — под собственным бюджетом, живущим строго вокруг вызова клиента и
            // уничтожаемым до фиксации. Причины те же, что в опросе сделок.
            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.RealtimeRest,
                MoexOperations.RealtimeOrderbookPoll,
                MoexDataKinds.Orderbook,
                state.Market,
                MoexFlows.Realtime);

            StorageInsertContext insertContext = new StorageInsertContext(
                operationTags.DataKind,
                operationTags.Market,
                MoexFlows.Realtime);

            RealtimeParsedPage page;
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
                    page = await client.GetOrderbookAsync(
                        state.Market, secid, fetchCts.Token);

                    TagList rowsTags = new TagList
                    {
                        { MoexTelemetryAttributes.Source, operationTags.Source },
                        { MoexTelemetryAttributes.DataKind, operationTags.DataKind },
                        { MoexTelemetryAttributes.Market, operationTags.Market },
                        { MoexTelemetryAttributes.Flow, operationTags.Flow },
                    };
                    MoexMetrics.RowsReceived.Add(page.Rows.Count, rowsTags);

                    MoexMetrics.RecordOperationSuccess(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                    pollActivity?.SetStatus(ActivityStatusCode.Ok);
                    // Остановка хоста — не отказ источника.
                    MoexMetrics.RecordOperationCancelled(
                        in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Cancelled);
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    fetchActivity?.SetStatus(ActivityStatusCode.Error);
                    pollActivity?.SetStatus(ActivityStatusCode.Error);
                    // Истёк собственный бюджет получения инструмента. Это отказ по тайм-ауту,
                    // а не отмена: хост работает, оборот продолжается со следующего инструмента.
                    MoexRealtimeReceiverLogMessages.OrderbookInstrumentFetchTimedOut(
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

            try
            {
                if (page.Rows.Count > 0)
                {
                    if (page.GuardFailure is not null) throw page.GuardFailure;

                    // Фиксация — под хостовым commitCt. У стакана курсора нет, durable-запись — только
                    // ClickHouse; за ней сразу двигается накопленный счётчик состояния.
                    await writer.WriteAsync(
                        spec,
                        secid,
                        page.Rows,
                        insertContext,
                        commitCt);

                    state.RowsTotal += page.Rows.Count;
                    state.LastConfirmedMarketTime = MoexTime.ToDateTimeOffset(page.FirstTime);
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
            => MoexRealtimeReceiverLogMessages.OrderbookSessionCloseFailed(
                _logger, exception, secid, sessionId);

        protected override void LogShutdownFailed(Exception exception)
            => MoexRealtimeReceiverLogMessages.OrderbookShutdownFailed(_logger, exception);

        protected override void LogInstrumentPreparationFailed(Exception exception, string secid)
            => MoexRealtimeReceiverLogMessages.OrderbookInstrumentPreparationFailed(
                _logger, exception, secid);

        protected override void LogInstrumentStopping(string secid, long sessionId)
            => MoexRealtimeReceiverLogMessages.OrderbookInstrumentStopping(
                _logger, secid, sessionId);

        protected override void LogInstrumentStopped(string secid, long sessionId, long rowsTotal)
            => MoexRealtimeReceiverLogMessages.OrderbookInstrumentStopped(
                _logger, secid, sessionId, rowsTotal);

    }

}
