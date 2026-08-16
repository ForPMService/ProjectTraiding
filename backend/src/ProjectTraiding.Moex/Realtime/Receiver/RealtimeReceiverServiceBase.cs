using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
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

        /// <summary>
        /// Закрытие оставшихся сеансов при завершении службы. Токен отмены сюда
        /// намеренно не передаётся, и по двум причинам сразу. При штатной остановке
        /// хостовый токен к этому моменту уже отменён, и его передача привела бы
        /// к молчаливому отказу закрытия. На любом другом пути выхода из цикла —
        /// например, если исключение выбросит сама запись об ошибке оборота —
        /// завершающая очистка тем более не должна зависеть от состояния токена.
        /// Реализация использует CancellationToken.None.
        /// </summary>
        protected abstract Task CloseSessionsAsync();

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
