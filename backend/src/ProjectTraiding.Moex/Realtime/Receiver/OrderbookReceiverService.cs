using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Buffers;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Realtime.Series;
using ProjectTraiding.Moex.Series;
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
    public sealed class OrderbookReceiverService : RealtimeReceiverServiceBase<ReceiverInstrumentState>
    {
        protected override string DataKind => "orderbook";

        protected override TimeSpan StalePollThreshold => _stalePollThreshold;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderbookReceiverService> _logger;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _stalePollThreshold;
        private bool _initialized;

        public OrderbookReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<OrderbookReceiverService> logger,
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

                IReadOnlyList<ReceiverInstrument> instruments =
                    await instrumentReader.GetEnabledForDataKindAsync(DataKind, ct);
                if (!_initialized)
                {
                    if (instruments.Count == 0)
                        MoexRealtimeReceiverLogMessages.OrderbookSubscriptionsEmpty(_logger);

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
                foreach (KeyValuePair<string, ReceiverInstrumentState> pair in States)
                {
                    try
                    {
                        await PollInstrumentAsync(
                            pair.Key,
                            pair.Value,
                            client,
                            executor,
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

        protected override Task OpenStateAsync(
            IServiceProvider services,
            ReceiverInstrument instrument,
            string boardId,
            CancellationToken ct)
        {
            States.Add(
                instrument.Secid,
                new ReceiverInstrumentState(
                    instrument.Market, boardId));
            MoexRealtimeReceiverLogMessages.OrderbookInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market);
            return Task.CompletedTask;
        }

        private async Task PollInstrumentAsync(
            string secid,
            ReceiverInstrumentState state,
            MoexRealtimeRestClient client,
            ClickHouseInsertExecutor executor,
            CancellationToken commitCt)
        {
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

            ProtobufBatchWriter<RealtimeOrderbookRow> writer = new(
                executor,
                MoexSeriesRegistry.RealtimeOrderbookTable,
                RealtimeOrderbookRow.InsertColumns,
                insertContext);
            int acceptedRowsCount;
            int rowFailuresCount;
            DateTime firstTime;
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
                        using (RentedBuffer body = await client.GetOrderbookAsync(
                                   state.Market, secid, fetchCts.Token))
                        {
                            MoexRealtimeParser.ParseOrderbook(
                                body.Span, secid, writer, _logger,
                                out _, out acceptedRowsCount,
                                out rowFailuresCount, out firstTime);
                        }
                        TagList rowsTags = new TagList
                        {
                            { MoexTelemetryAttributes.Source, operationTags.Source },
                            { MoexTelemetryAttributes.DataKind, operationTags.DataKind },
                            { MoexTelemetryAttributes.Market, operationTags.Market },
                            { MoexTelemetryAttributes.Flow, operationTags.Flow },
                        };
                        MoexMetrics.RowsReceived.Add(acceptedRowsCount, rowsTags);
                        MoexMetrics.RecordOperationSuccess(
                            in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                        fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                    }
                    catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                    {
                        fetchActivity?.SetStatus(ActivityStatusCode.Ok);
                        MoexMetrics.RecordOperationCancelled(
                            in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                        throw;
                    }
                    catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                    {
                        fetchActivity?.SetStatus(ActivityStatusCode.Error);
                        pollActivity?.SetStatus(ActivityStatusCode.Error);
                        MoexRealtimeReceiverLogMessages.OrderbookInstrumentFetchTimedOut(
                            _logger, secid, state.Market, _instrumentFetchTimeout);
                        MoexMetrics.RecordOperationTimeout(
                            in operationTags, Stopwatch.GetElapsedTime(fetchStart).TotalSeconds);
                        MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (ex is MoexSchemaMismatchException)
                        {
                            MoexLogMessages.ParseFailed(
                                _logger, ex,
                                BuildOrderbookEndpoint(state.Market, secid),
                                MoexErrorTypes.SchemaMismatch, ex.Message);
                        }
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
                    if (rowFailuresCount > 0)
                        throw new InvalidOperationException("Снимок стакана обработан частично.");

                    if (writer.RowCount > 0)
                    {
                        int expectedRows = writer.RowCount;
                        long insertedRows = await writer.FlushAsync(commitCt);
                        if (insertedRows != expectedRows)
                        {
                            throw new InvalidOperationException(
                                $"ClickHouse подтвердил запись {insertedRows} из {expectedRows} строк стакана.");
                        }
                        state.RowsTotal += insertedRows;
                        state.LastConfirmedMarketTime = MoexTime.ToDateTimeOffset(firstTime);
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
                catch (Exception ex)
                {
                    pollActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    MoexMetrics.RecordRealtimePoll(in operationTags, MoexOutcomes.Error);
                    throw;
                }
            }

        }

        private static string BuildOrderbookEndpoint(string market, string secid)
        {
            if (market == MoexMarkets.Stock)
                return $"/engines/stock/markets/shares/boards/TQBR/securities/{secid}/orderbook.json";
            return $"/engines/futures/markets/forts/boards/RFUD/securities/{secid}/orderbook.json";
        }

        protected override void LogInstrumentPreparationFailed(Exception exception, string secid)
            => MoexRealtimeReceiverLogMessages.OrderbookInstrumentPreparationFailed(
                _logger, exception, secid);

        protected override void LogInstrumentStopped(string secid, long rowsTotal)
            => MoexRealtimeReceiverLogMessages.OrderbookInstrumentStopped(
                _logger, secid, rowsTotal);

    }

}
