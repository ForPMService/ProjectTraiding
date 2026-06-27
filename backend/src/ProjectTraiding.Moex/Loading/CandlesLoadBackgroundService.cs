using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Фоновый исполнитель загрузок: подбирает ожидающие свечные задачи из moex_load_tasks
    /// и гонит их через CandlesLoadRunner. Убирает ручной запуск — оператор создаёт задачу
    /// командой Management, исполнитель сам её подхватывает.
    /// Берёт зависимости через область видимости на итерацию (сам — одиночка, runner/reader — Transient).
    /// Прерывание корректно на границе пачки (токен прокинут вглубь загрузки).
    /// Сбой одной задачи не валит сервис: RunAsync помечает error, исполнитель идёт дальше.
    /// </summary>
    public sealed class CandlesLoadBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CandlesLoadBackgroundService> _logger;
        private readonly TimeSpan _pollInterval;

        public CandlesLoadBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<CandlesLoadBackgroundService> logger,
            TimeSpan pollInterval)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            MoexLoadTaskLogMessages.BackgroundStarted(_logger, _pollInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool worked;
                try
                {
                    worked = await TryRunOneAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // штатная остановка хоста — не ошибка
                }
                catch (Exception ex)
                {
                    // Сбой самого цикла (подбора), не сбой задачи. Не валим сервис.
                    MoexLoadTaskLogMessages.BackgroundPollFailed(_logger, ex);
                    worked = false;
                }

                // Пусто — ждём интервал; была задача — сразу следующая (очередь могла накопиться).
                if (!worked)
                {
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

            MoexLoadTaskLogMessages.BackgroundStopped(_logger);
        }

        // true — задача была (выполнена или помечена error); false — очередь пуста.
        private async Task<bool> TryRunOneAsync(CancellationToken ct)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

            MoexLoadTaskReader reader = scope.ServiceProvider.GetRequiredService<MoexLoadTaskReader>();
            Guid? taskId = await reader.GetNextPendingCandlesTaskIdAsync(ct);
            if (taskId is null)
                return false;

            CandlesLoadRunner runner = scope.ServiceProvider.GetRequiredService<CandlesLoadRunner>();
            try
            {
                await runner.RunAsync(taskId.Value, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Остановка посреди загрузки. RunAsync уже пометил задачу cancelled.
                // Пробрасываем, чтобы внешний цикл штатно завершился.
                throw;
            }
            catch (Exception)
            {
                // Сбой загрузки. RunAsync уже пометил задачу error и записал причину.
                // Исполнитель не падает — берёт следующую.
                MoexLoadTaskLogMessages.BackgroundTaskFailed(_logger, taskId.Value);
            }

            return true;
        }
    }
}
