using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Внешний жизненный цикл фоновой службы приёма текущих данных: цикл оборотов,
    /// различение хостовой отмены и сбоя, задержка между оборотами.
    /// Предметная часть — подписки, состояния, курсоры и опрос — принадлежит наследникам.
    ///
    /// Журналирование запуска, ошибки оборота и остановки вынесено в абстрактные члены
    /// намеренно: у каждой службы собственные стабильные EventId и EventName, входящие
    /// в наблюдаемый контракт. Базовый класс задаёт порядок и момент записи, но не
    /// подменяет сами события.
    /// </summary>
    public abstract class RealtimeReceiverServiceBase<TState> : BackgroundService
        where TState : ReceiverInstrumentState
    {
        private const string StockMarket = "stock";
        private const string FuturesMarket = "futures";
        private const string StockBoardId = "TQBR";
        private const string FuturesBoardId = "RFUD";

        private readonly TimeSpan _pollInterval;

        // Рабочие наборы одного оборота согласования подписок и публикации снимков.
        // Принадлежат одному обороту одной службы: опрос инструментов внутри оборота
        // последователен. При появлении параллельного опроса (пункт 8 плана) эти поля
        // обязаны быть пересмотрены раньше, чем параллелизм включится.
        private readonly HashSet<string> _desired = new(StringComparer.Ordinal);
        private readonly List<string> _stopping = new();
        private readonly List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> _snapshots =
            new(2);

        /// <summary>
        /// Состояния по коду инструмента. Ключи изменяются только в методах
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
                // Собственного отрезка трассы у оборота нет намеренно. Он не отвечал ни
                // на один вопрос оператора: свежесть приёма показывают метрики возраста
                // опроса и данных, предметные действия — вложенные отрезки опроса
                // инструмента и обращения к источнику, отказ оборота — событие журнала
                // уровня ошибки. Отрезок сообщал лишь, что случился очередной оборот,
                // и в простое такие отрезки составляли весь поток трасс службы.
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
                        LogTurnFailed(ex);
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
                States.Clear();
                RealtimeTelemetryState.ReplaceForDataKind(DataKind, []);
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
        /// Подготавливает один инструмент и кладёт состояние в словарь.
        /// Остаётся у каждой службы своим: у сделок сюда входят чтение курсора
        /// и холодный старт по хвосту.
        /// </summary>
        protected abstract Task OpenStateAsync(
            IServiceProvider services,
            ReceiverInstrument instrument,
            string boardId,
            CancellationToken ct);

        /// <summary>Событие неудачной подготовки инструмента со стабильным EventId наследника.</summary>
        protected abstract void LogInstrumentPreparationFailed(Exception exception, string secid);

        protected abstract void LogInstrumentStopped(string secid, long rowsTotal);

        protected Task PrepareInitialInstrumentsAsync(
            IServiceProvider services, IReadOnlyList<ReceiverInstrument> instruments, CancellationToken ct)
            => AddNewInstrumentsAsync(services, instruments, ct);

        protected async Task ReconcileStatesAsync(
            IServiceProvider services, IReadOnlyList<ReceiverInstrument> instruments, CancellationToken ct)
        {
            _desired.Clear();
            foreach (ReceiverInstrument instrument in instruments)
                _desired.Add(instrument.Secid);

            _stopping.Clear();
            foreach (KeyValuePair<string, TState> pair in States)
                if (!_desired.Contains(pair.Key))
                    _stopping.Add(pair.Key);

            foreach (string secid in _stopping)
            {
                TState state = States[secid];
                States.Remove(secid);
                LogInstrumentStopped(secid, state.RowsTotal);
            }

            await AddNewInstrumentsAsync(services, instruments, ct);
        }

        private async Task AddNewInstrumentsAsync(
            IServiceProvider services, IReadOnlyList<ReceiverInstrument> instruments, CancellationToken ct)
        {
            foreach (ReceiverInstrument instrument in instruments)
            {
                if (States.ContainsKey(instrument.Secid))
                    continue;
                try
                {
                    await OpenStateAsync(services, instrument, GetBoardId(instrument.Market), ct);
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
            _snapshots.Clear();

            AddTelemetrySnapshotsForMarket(MoexMarkets.Stock, now, _snapshots);
            AddTelemetrySnapshotsForMarket(MoexMarkets.Futures, now, _snapshots);

            RealtimeTelemetryState.ReplaceForDataKind(DataKind, _snapshots);
        }

        /// <summary>
        /// Добавляет снимок рынка. Свечам разрешено добавить bounded интервальные разрезы;
        /// остальные приёмники сохраняют исходный разрез data_kind/market.
        /// </summary>
        protected virtual void AddTelemetrySnapshotsForMarket(
            string market,
            DateTimeOffset now,
            List<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> snapshots)
        {
            AddTelemetrySnapshot(market, now, snapshots);
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
                if (state.Market != market)
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
