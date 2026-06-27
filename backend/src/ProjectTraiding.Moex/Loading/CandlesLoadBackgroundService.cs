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
        private readonly int _concurrency;

        public CandlesLoadBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<CandlesLoadBackgroundService> logger,
            TimeSpan pollInterval,
            int concurrency)
        {
            if (concurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(concurrency), "Число дорожек должно быть положительным.");

            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
            _concurrency = concurrency;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            MoexLoadTaskLogMessages.BackgroundStarted(_logger, _pollInterval);

            Task[] lanes = new Task[_concurrency];
            for (int i = 0; i < _concurrency; i++)
                lanes[i] = RunLaneAsync(stoppingToken);

            await Task.WhenAll(lanes);

            MoexLoadTaskLogMessages.BackgroundStopped(_logger);
        }

        // Одна дорожка — последовательный конвейер: подобрать задачу с захватом и пропуском
        // занятых, прогнать через координатор, повторить. Пусто — пауза и снова.
        // Общий ограничитель частоты держит суммарный темп независимо от числа дорожек.
        private async Task RunLaneAsync(CancellationToken stoppingToken)
        {
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
                    // Сбой самого цикла (подбора), не сбой задачи. Не валим дорожку.
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
        }

        // true — задача была (выполнена или помечена error); false — очередь пуста.
        private async Task<bool> TryRunOneAsync(CancellationToken ct)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

            MoexLoadTaskReader reader = scope.ServiceProvider.GetRequiredService<MoexLoadTaskReader>();
            Guid? taskId = await reader.ClaimNextPendingTaskIdAsync(ct);
            if (taskId is null)
                return false;

            LoadRunner runner = scope.ServiceProvider.GetRequiredService<LoadRunner>();
            try
            {
                await runner.RunAsync(taskId.Value, ct, alreadyClaimed: true);
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
