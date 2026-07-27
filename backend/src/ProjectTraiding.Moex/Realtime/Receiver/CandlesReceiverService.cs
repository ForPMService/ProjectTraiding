using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Периодический приём свечей текущего торгового дня по всем подписанным инструментам.
    /// Реальное время — только минутная свеча (интервал 1): её растущую минуту ждёт график.
    /// Закрытые минуты пишутся в moex_candles_1m с приоритетом приёма 0 (историческая загрузка
    /// перекрывает при слиянии); текущая незакрытая минута идёт только в оперативное хранилище —
    /// в ClickHouse её писать нельзя, она ещё меняется. Курсора у свечей в базе нет; повтор записи
    /// закрытых минут в пределах запуска отсекает граница LastClosedBegin в состоянии.
    /// Желаемый список подписок сверяется на каждом обороте: снятые инструменты исключаются
    /// из опроса немедленно, а их сеансы покрытия закрываются штатно без перезапуска процесса —
    /// так же, как у сделок и стакана.
    /// </summary>
    public sealed class CandlesReceiverService : BackgroundService
    {
        private const string StockMarket = "stock";
        private const string FuturesMarket = "futures";
        private const string StockBoardId = "TQBR";
        private const string FuturesBoardId = "RFUD";
        private const string DataKind = "candles";

        // Реальное время пока только минутная свеча. Контракт подписок (V026) держит это CHECK-ом.
        private const int CandleInterval = 1;

        // Хвост окна опроса, минуты. Одна-две минуты укладываются в одну страницу клиента; запас
        // в несколько минут страхует от пропущенного оборота и остаётся в пределах одной страницы.
        // Историю торгового дня добирает исторический загрузчик, здесь только текущий хвост.
        private const int WindowMinutes = 3;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CandlesReceiverService> _logger;
        private readonly TimeSpan _pollInterval;
        private readonly TimeSpan _instrumentFetchTimeout;
        private readonly TimeSpan _heartbeatMinInterval;
        private readonly Dictionary<string, CandleInstrumentState> _states =
            new Dictionary<string, CandleInstrumentState>();
        private bool _initialized;

        public CandlesReceiverService(
            IServiceScopeFactory scopeFactory,
            ILogger<CandlesReceiverService> logger,
            TimeSpan pollInterval,
            TimeSpan instrumentFetchTimeout,
            TimeSpan heartbeatMinInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
            _instrumentFetchTimeout = instrumentFetchTimeout;
            _heartbeatMinInterval = heartbeatMinInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            MoexRealtimeReceiverLogMessages.CandlesStarted(_logger, _pollInterval);

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
                        MoexRealtimeReceiverLogMessages.CandlesTurnFailed(_logger, ex);
                    }

                    try
                    {
                        await Task.Delay(_pollInterval, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await CloseSessionsAsync();
                MoexRealtimeReceiverLogMessages.CandlesStopped(_logger);
            }
        }

        private async Task RunTurnAsync(CancellationToken ct)
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

                await PrepareInitialInstrumentsAsync(instruments, coverageWriter, ct);
                _initialized = true;
            }
            else
            {
                await ReconcileStatesAsync(instruments, coverageWriter, ct);
            }

            MoexRealtimeRestClient client =
                scope.ServiceProvider.GetRequiredService<MoexRealtimeRestClient>();
            RealtimeRowWriter<CandlesDTO> writer =
                scope.ServiceProvider.GetRequiredService<RealtimeRowWriter<CandlesDTO>>();
            RealtimeLatestWriter latestWriter =
                scope.ServiceProvider.GetRequiredService<RealtimeLatestWriter>();

            foreach (KeyValuePair<string, CandleInstrumentState> pair in _states)
            {
                if (pair.Value.IsStopping)
                    continue;

                try
                {
                    await PollInstrumentAsync(
                        pair.Key, pair.Value, client, writer, latestWriter, coverageWriter, ct);
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

        private async Task PrepareInitialInstrumentsAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            // Один запрос закрывает ВСЕ осиротевшие 'open'-сеансы свечей (интервал 1) прошлого
            // запуска, независимо от инструмента — по единственному писателю этого вида и интервала.
            await coverageWriter.MarkOrphanedOpenAsCrashedAsync(DataKind, CandleInterval, ct);

            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (_states.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await OpenStateAsync(instrument, boardId, coverageWriter, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MoexRealtimeReceiverLogMessages.CandlesInstrumentPreparationFailed(
                        _logger, ex, instrument.Secid);
                }
            }
        }

        /// <summary>
        /// Приводит словарь состояний к желаемому списку подписок. Снятые инструменты
        /// помечаются к остановке, исключаются из опроса и закрываются штатно; состояние
        /// удаляется только после успешного закрытия, при отказе закрытие повторяется на
        /// следующем обороте. Затем добавляются новые. Пустой желаемый список означает
        /// остановку всех состояний этого вида — это законное состояние, а не сбой.
        /// </summary>
        private async Task ReconcileStatesAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            HashSet<string> desired = new(StringComparer.Ordinal);
            for (int i = 0; i < instruments.Count; i++)
                desired.Add(instruments[i].Secid);

            // Пометка. Изменяем только значения, ключи словаря не трогаем — перечисление безопасно.
            foreach (KeyValuePair<string, CandleInstrumentState> pair in _states)
            {
                if (desired.Contains(pair.Key) || pair.Value.IsStopping)
                    continue;

                pair.Value.IsStopping = true;
                MoexRealtimeReceiverLogMessages.CandlesInstrumentStopping(
                    _logger, pair.Key, pair.Value.SessionId);
            }

            // Ключи собираем заранее: удалять из словаря во время перечисления нельзя.
            List<string> stopping = new();
            foreach (KeyValuePair<string, CandleInstrumentState> pair in _states)
            {
                if (pair.Value.IsStopping)
                    stopping.Add(pair.Key);
            }

            for (int i = 0; i < stopping.Count; i++)
            {
                string secid = stopping[i];
                CandleInstrumentState state = _states[secid];
                try
                {
                    await coverageWriter.CloseSessionAsync(state.SessionId, state.RowsTotal, ct);
                    _states.Remove(secid);
                    MoexRealtimeReceiverLogMessages.CandlesInstrumentStopped(
                        _logger, secid, state.SessionId, state.RowsTotal);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Состояние остаётся с признаком остановки: в опрос не попадёт,
                    // закрытие повторится на следующем обороте.
                    MoexRealtimeReceiverLogMessages.CandlesSessionCloseFailed(
                        _logger, ex, secid, state.SessionId);
                }
            }

            await AddNewInstrumentsAsync(instruments, coverageWriter, ct);
        }

        private async Task AddNewInstrumentsAsync(
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (_states.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await coverageWriter.CloseCrashedAsync(
                        instrument.Secid, instrument.Market, boardId, DataKind, CandleInterval, ct);
                    await OpenStateAsync(instrument, boardId, coverageWriter, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MoexRealtimeReceiverLogMessages.CandlesInstrumentPreparationFailed(
                        _logger, ex, instrument.Secid);
                }
            }
        }

        private async Task OpenStateAsync(
            ReceiverInstrument instrument,
            string boardId,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            long sessionId = await coverageWriter.OpenSessionAsync(
                instrument.Secid, instrument.Market, boardId, DataKind, CandleInterval, ct);
            long heartbeatTimestamp = Stopwatch.GetTimestamp();
            _states.Add(
                instrument.Secid,
                new CandleInstrumentState(
                    sessionId, instrument.Market, boardId, heartbeatTimestamp));
            MoexRealtimeReceiverLogMessages.CandlesInstrumentPrepared(
                _logger, instrument.Secid, instrument.Market, sessionId);
        }

        private async Task PollInstrumentAsync(
            string secid,
            CandleInstrumentState state,
            MoexRealtimeRestClient client,
            RealtimeRowWriter<CandlesDTO> writer,
            RealtimeLatestWriter latestWriter,
            StreamCoverageWriter coverageWriter,
            CancellationToken commitCt)
        {
            // Московское время фиксируем один раз: им задаётся и окно запроса, и граница «закрыта».
            DateTime now = MoexTime.Now;
            DateTime from = now.AddMinutes(-WindowMinutes);

            // Получение окна свечей — под собственным бюджетом, живущим строго вокруг вызова клиента
            // и уничтожаемым до фиксации. Причины те же, что в стакане: таймер бюджета не должен
            // сработать во время записи и выдать сбой хранилища за тайм-аут получения.
            List<CandlesDTO> candles;
            using (CancellationTokenSource fetchCts =
                   CancellationTokenSource.CreateLinkedTokenSource(commitCt))
            {
                fetchCts.CancelAfter(_instrumentFetchTimeout);
                try
                {
                    if (state.Market == StockMarket)
                        candles = await client.GetCandlesTodayStockAsync(
                            secid, from, now, CandleInterval, fetchCts.Token);
                    else
                        candles = await client.GetCandlesTodayFuturesAsync(
                            secid, from, now, CandleInterval, fetchCts.Token);
                }
                catch (OperationCanceledException) when (commitCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
                {
                    MoexRealtimeReceiverLogMessages.CandlesInstrumentFetchTimedOut(
                        _logger, secid, state.Market, _instrumentFetchTimeout);
                    return;
                }
            }

            // Разделение на закрытые минуты и последнюю известную. Минута с началом B закрыта,
            // когда московское время достигло B + интервал. Закрытые пишем в ClickHouse, но только
            // новее уже записанной границы, чтобы не гонять повтор. Отдельно отслеживаем последнюю
            // известную свечу ответа (максимум по Begin) — её, закрытую или растущую, кладём в
            // оперативное хранилище.
            List<CandlesDTO> closed = new List<CandlesDTO>(candles.Count);
            CandlesDTO? latestKnown = null;
            DateTime latestKnownBegin = DateTime.MinValue;
            DateTime maxClosedBegin = state.LastClosedBegin ?? DateTime.MinValue;

            for (int i = 0; i < candles.Count; i++)
            {
                CandlesDTO candle = candles[i];
                if (candle.Begin is null)
                    continue;

                DateTime begin = candle.Begin.Value;

                if (begin > latestKnownBegin)
                {
                    latestKnownBegin = begin;
                    latestKnown = candle;
                }

                bool isClosed = begin.AddMinutes(CandleInterval) <= now;
                if (!isClosed)
                    continue;

                if (state.LastClosedBegin is null || begin > state.LastClosedBegin.Value)
                {
                    closed.Add(candle);
                    if (begin > maxClosedBegin)
                        maxClosedBegin = begin;
                }
            }

            if (closed.Count > 0)
            {
                // Фиксация — под хостовым commitCt. У свечей курсора в базе нет; durable-запись —
                // только ClickHouse, за ней сразу двигаются граница и счётчик состояния. Запись
                // предварительна (приоритет 0): источник истины — исторический загрузчик (приоритет 1),
                // он перекрывает минуту при слиянии. До планировщика догрузки текущего дня
                // предварительная версия может дожить до ручного прогона; точные расчёты обязаны
                // считать строки приёмника предварительными.
                await writer.WriteAsync(secid, closed, null, commitCt);
                state.RowsTotal += closed.Count;
                state.LastClosedBegin = maxClosedBegin;
            }

            await HeartbeatIfDueAsync(state, coverageWriter, commitCt);

            // Оперативное хранилище — best-effort, последним действием: в ключ кладётся последняя
            // известная свеча ответа (закрытая или растущая); писатель сам проглатывает сбой.
            if (latestKnown is not null)
                await latestWriter.WriteLatestCandleAsync(secid, latestKnown, commitCt);
        }

        private async Task HeartbeatIfDueAsync(
            CandleInstrumentState state,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            if (Stopwatch.GetElapsedTime(state.LastHeartbeatTimestamp) < _heartbeatMinInterval)
                return;

            await coverageWriter.HeartbeatAsync(state.SessionId, state.RowsTotal, ct);
            state.LastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        }

        private async Task CloseSessionsAsync()
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                foreach (KeyValuePair<string, CandleInstrumentState> pair in _states)
                {
                    try
                    {
                        await coverageWriter.CloseSessionAsync(
                            pair.Value.SessionId, pair.Value.RowsTotal, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        MoexRealtimeReceiverLogMessages.CandlesSessionCloseFailed(
                            _logger, ex, pair.Key, pair.Value.SessionId);
                    }
                }
            }
            catch (Exception ex)
            {
                MoexRealtimeReceiverLogMessages.CandlesShutdownFailed(_logger, ex);
            }
        }

        private static string GetBoardId(string market)
        {
            if (market == StockMarket)
                return StockBoardId;
            if (market == FuturesMarket)
                return FuturesBoardId;

            throw new InvalidOperationException(
                $"Неизвестный рынок инструмента приёмника: '{market}'.");
        }
    }

    internal sealed class CandleInstrumentState
    {
        public CandleInstrumentState(
            long sessionId, string market, string boardId, long lastHeartbeatTimestamp)
        {
            SessionId = sessionId;
            Market = market;
            BoardId = boardId;
            LastHeartbeatTimestamp = lastHeartbeatTimestamp;
        }

        public long SessionId { get; }
        public string Market { get; }
        public string BoardId { get; }
        public long RowsTotal { get; set; }
        public long LastHeartbeatTimestamp { get; set; }
        public DateTime? LastClosedBegin { get; set; }

        /// <summary>
        /// Подписка снята оператором: инструмент исключён из опроса, сеанс покрытия ждёт
        /// штатного закрытия. Состояние удаляется из словаря только после успешного закрытия.
        /// Обратно в активное не возвращается — иначе сеанс перекрыл бы период отключения.
        /// </summary>
        public bool IsStopping { get; set; }
    }
}
