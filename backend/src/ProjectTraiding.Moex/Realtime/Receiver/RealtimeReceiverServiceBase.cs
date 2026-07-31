using Microsoft.Extensions.Hosting;

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
    public abstract class RealtimeReceiverServiceBase : BackgroundService
    {
        private readonly TimeSpan _pollInterval;

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
                await CloseSessionsAsync();
                LogStopped();
            }
        }

        /// <summary>Один оборот приёма. Вся предметная механика — здесь.</summary>
        protected abstract Task RunTurnAsync(CancellationToken ct);

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
    }
}
