using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Внешний жизненный цикл фоновой службы приёма текущих данных: цикл оборотов,
    /// различение хостовой отмены и сбоя, задержка между оборотами, закрытие сеансов
    /// при остановке. Предметная часть — подписки, состояния, курсоры, опрос, писатели
    /// и сердцебиение — целиком принадлежит наследникам.
    ///
    /// Журналирование запуска, ошибки оборота и остановки вынесено в абстрактные члены
    /// намеренно: у каждой службы собственные стабильные EventId и EventName, входящие
    /// в наблюдаемый контракт. Базовый класс задаёт порядок и момент записи, но не
    /// подменяет сами события.
    /// </summary>
    public abstract class RealtimeReceiverServiceBase<TState> : BackgroundService
        where TState : ReceiverInstrumentSessionState
    {
        private const string StockMarket = "stock";
        private const string FuturesMarket = "futures";
        private const string StockBoardId = "TQBR";
        private const string FuturesBoardId = "RFUD";

        private readonly TimeSpan _pollInterval;

        /// <summary>
        /// Состояния сеансов по коду инструмента. Ключи изменяются только в методах
        /// согласования подписок; предметный опрос меняет лишь значения.
        /// </summary>
        protected readonly Dictionary<string, TState> States =
            new Dictionary<string, TState>();

        protected RealtimeReceiverServiceBase(TimeSpan pollInterval)
        {
            _pollInterval = pollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LogStarted(_pollInterval);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using (Activity? turnActivity =
                           MoexTelemetry.ActivitySource.StartActivity("moex.realtime.turn"))
                    {
                    try
                    {
                        await RunTurnAsync(stoppingToken);
                        turnActivity?.SetStatus(ActivityStatusCode.Ok);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        turnActivity?.SetStatus(ActivityStatusCode.Ok);
                        break;
                    }
                    catch (Exception ex)
                    {
                        turnActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        LogTurnFailed(ex);
                    }
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
                LogStopped();
            }
        }

        /// <summary>Один оборот приёма. Вся предметная механика — здесь.</summary>
        protected abstract Task RunTurnAsync(CancellationToken ct);

        /// <summary>
        /// Вид данных приёма: "trades", "orderbook" или "candles". Одно значение служит
        /// и разрезом телеметрии, и значением столбца в PostgreSQL. Второго поля под
        /// телеметрическое имя нет намеренно: у видов данных приёма преобразование
        /// MoexDataKinds.FromTaskDataKind тождественно, особое значение там одно —
        /// оповещения, а их приём не ведёт. Чем меньше независимых истин, тем меньше
        /// мест, где они разойдутся.
        /// </summary>
        protected abstract string DataKind { get; }

        /// <summary>Порог, после которого инструмент считается отставшим.</summary>
        protected abstract TimeSpan StalePollThreshold { get; }

        /// <summary>
        /// Свечной интервал для писателей покрытия или пусто у видов данных без интервала.
        /// </summary>
        protected abstract int? StorageCandleInterval { get; }

        /// <summary>
        /// Открывает сеанс по одному инструменту и кладёт состояние в словарь.
        /// Остаётся у каждой службы своим: у сделок сюда входят чтение курсора
        /// и холодный старт по хвосту, у стакана и свечей — только открытие сеанса.
        /// </summary>
        protected abstract Task OpenStateAsync(
            IServiceProvider services,
            ReceiverInstrument instrument,
            string boardId,
            CancellationToken ct);

        /// <summary>Событие неудачной подготовки инструмента со стабильным EventId наследника.</summary>
        protected abstract void LogInstrumentPreparationFailed(Exception exception, string secid);

        /// <summary>Событие пометки инструмента к остановке со стабильным EventId наследника.</summary>
        protected abstract void LogInstrumentStopping(string secid, long sessionId);

        /// <summary>Событие штатной остановки инструмента со стабильным EventId наследника.</summary>
        protected abstract void LogInstrumentStopped(string secid, long sessionId, long rowsTotal);

        /// <summary>
        /// Первичная подготовка при первом обороте. Один запрос закрывает все осиротевшие
        /// открытые сеансы прошлого запуска независимо от инструмента: приёмник читает
        /// только включённые подписки, поэтому точечное закрытие пропустило бы осиротевший
        /// сеанс отключённой или удалённой подписки.
        /// </summary>
        protected async Task PrepareInitialInstrumentsAsync(
            IServiceProvider services,
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            await coverageWriter.MarkOrphanedOpenAsCrashedAsync(
                DataKind, StorageCandleInterval, ct);

            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (States.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await OpenStateAsync(services, instrument, boardId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogInstrumentPreparationFailed(ex, instrument.Secid);
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
        protected async Task ReconcileStatesAsync(
            IServiceProvider services,
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            HashSet<string> desired = new(StringComparer.Ordinal);
            for (int i = 0; i < instruments.Count; i++)
                desired.Add(instruments[i].Secid);

            // Пометка. Изменяем только значения, ключи словаря не трогаем — перечисление безопасно.
            foreach (KeyValuePair<string, TState> pair in States)
            {
                if (desired.Contains(pair.Key) || pair.Value.IsStopping)
                    continue;

                pair.Value.IsStopping = true;
                LogInstrumentStopping(pair.Key, pair.Value.SessionId);
            }

            // Ключи собираем заранее: удалять из словаря во время перечисления нельзя.
            List<string> stopping = new();
            foreach (KeyValuePair<string, TState> pair in States)
            {
                if (pair.Value.IsStopping)
                    stopping.Add(pair.Key);
            }

            for (int i = 0; i < stopping.Count; i++)
            {
                string secid = stopping[i];
                TState state = States[secid];
                try
                {
                    await coverageWriter.CloseSessionAsync(state.SessionId, state.RowsTotal, ct);
                    States.Remove(secid);
                    LogInstrumentStopped(secid, state.SessionId, state.RowsTotal);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Состояние остаётся с признаком остановки: в опрос не попадёт,
                    // закрытие повторится на следующем обороте.
                    LogSessionCloseFailed(ex, secid, state.SessionId);
                }
            }

            await AddNewInstrumentsAsync(services, instruments, coverageWriter, ct);
        }

        private async Task AddNewInstrumentsAsync(
            IServiceProvider services,
            IReadOnlyList<ReceiverInstrument> instruments,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            for (int i = 0; i < instruments.Count; i++)
            {
                ReceiverInstrument instrument = instruments[i];
                if (States.ContainsKey(instrument.Secid))
                    continue;

                try
                {
                    string boardId = GetBoardId(instrument.Market);
                    await coverageWriter.CloseCrashedAsync(
                        instrument.Secid, instrument.Market, boardId,
                        DataKind, StorageCandleInterval, ct);
                    await OpenStateAsync(services, instrument, boardId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogInstrumentPreparationFailed(ex, instrument.Secid);
                }
            }
        }

        /// <summary>
        /// Публикует агрегат состояния приёма по обоим рынкам. Вызывается после любого
        /// исхода оборота: иначе при отказе согласования состояний числа активных
        /// и отставших инструментов застынут на прежних значениях.
        /// </summary>
        protected void PublishTelemetrySnapshots()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> snapshots =
                new List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>>(2);

            AddTelemetrySnapshot(MoexMarkets.Stock, now, snapshots);
            AddTelemetrySnapshot(MoexMarkets.Futures, now, snapshots);

            RealtimeTelemetryState.ReplaceForDataKind(DataKind, snapshots);
        }

        private void AddTelemetrySnapshot(
            string market,
            DateTimeOffset now,
            List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> snapshots)
        {
            DateTimeOffset? lastSuccessfulPollTime = null;
            DateTimeOffset? lastConfirmedMarketTime = null;
            long activeInstruments = 0;
            long staleInstruments = 0;

            foreach (KeyValuePair<string, TState> pair in States)
            {
                TState state = pair.Value;
                if (state.IsStopping || state.Market != market)
                    continue;

                activeInstruments++;

                DateTimeOffset? successfulPollTime = state.LastSuccessfulPollTime;
                if (successfulPollTime is null ||
                    now - successfulPollTime.Value > StalePollThreshold)
                {
                    staleInstruments++;
                }

                if (successfulPollTime is not null &&
                    (lastSuccessfulPollTime is null ||
                     successfulPollTime.Value > lastSuccessfulPollTime.Value))
                {
                    lastSuccessfulPollTime = successfulPollTime;
                }

                DateTimeOffset? confirmedMarketTime = state.LastConfirmedMarketTime;
                if (confirmedMarketTime is not null &&
                    (lastConfirmedMarketTime is null ||
                     confirmedMarketTime.Value > lastConfirmedMarketTime.Value))
                {
                    lastConfirmedMarketTime = confirmedMarketTime;
                }
            }

            if (activeInstruments == 0)
                return;

            snapshots.Add(new KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>(
                new RealtimeTelemetryKey(DataKind, market),
                new RealtimeTelemetrySnapshot(
                    lastSuccessfulPollTime,
                    lastConfirmedMarketTime,
                    activeInstruments,
                    staleInstruments)));
        }

        /// <summary>Фабрика областей видимости для доступа к писателям в фоновом потоке.</summary>
        protected abstract IServiceScopeFactory ScopeFactory { get; }

        /// <summary>Событие неудачного закрытия сеанса со стабильным EventId наследника.</summary>
        protected abstract void LogSessionCloseFailed(Exception exception, string secid, long sessionId);

        /// <summary>Событие отказа завершающей очистки со стабильным EventId наследника.</summary>
        protected abstract void LogShutdownFailed(Exception exception);

        /// <summary>
        /// Закрытие оставшихся сеансов при завершении службы. Токен отмены сюда
        /// намеренно не передаётся, и по двум причинам сразу. При штатной остановке
        /// хостовый токен к этому моменту уже отменён, и его передача привела бы
        /// к молчаливому отказу закрытия. На любом другом пути выхода из цикла —
        /// например, если исключение выбросит сама запись об ошибке оборота —
        /// завершающая очистка тем более не должна зависеть от состояния токена.
        /// </summary>
        protected virtual async Task CloseSessionsAsync()
        {
            try
            {
                await using AsyncServiceScope scope = ScopeFactory.CreateAsyncScope();
                StreamCoverageWriter coverageWriter =
                    scope.ServiceProvider.GetRequiredService<StreamCoverageWriter>();

                foreach (KeyValuePair<string, TState> pair in States)
                {
                    try
                    {
                        await coverageWriter.CloseSessionAsync(
                            pair.Value.SessionId,
                            pair.Value.RowsTotal,
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        LogSessionCloseFailed(ex, pair.Key, pair.Value.SessionId);
                    }
                }
            }
            catch (Exception ex)
            {
                LogShutdownFailed(ex);
            }
        }

        /// <summary>Событие запуска службы со стабильным EventId наследника.</summary>
        protected abstract void LogStarted(TimeSpan pollInterval);

        /// <summary>Событие неожиданной ошибки оборота со стабильным EventId наследника.</summary>
        protected abstract void LogTurnFailed(Exception exception);

        /// <summary>Событие остановки службы со стабильным EventId наследника.</summary>
        protected abstract void LogStopped();

        /// <summary>
        /// Доска торгов по рынку. Постоянна для рынка у всех трёх видов данных приёма.
        /// Ветки перечислены явно, без отката к одному из рынков: молчаливый откат
        /// превратил бы опечатку в рынке в обращение не по той доске.
        /// </summary>
        protected static string GetBoardId(string market)
        {
            if (market == StockMarket)
                return StockBoardId;
            if (market == FuturesMarket)
                return FuturesBoardId;

            throw new InvalidOperationException(
                $"Неизвестный рынок инструмента приёмника: '{market}'.");
        }
    }
}
