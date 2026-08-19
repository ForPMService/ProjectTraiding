using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Фоновый исполнитель загрузок: подбирает ожидающие задачи любого вида из moex_load_tasks
    /// и гонит их через LoadRunner. Оператор создаёт задачу командой Management, исполнитель
    /// сам её подхватывает.
    /// Читатель задач — одиночка и приходит через конструктор: захват выполняется до создания
    /// области, поэтому пустая очередь не стоит ни области, ни её служб. Область создаётся
    /// только под координатор и только после успешного захвата.
    /// Прерывание корректно на границе пачки (токен прокинут вглубь загрузки).
    /// Сбой одной задачи не валит сервис: RunAsync помечает error, исполнитель идёт дальше.
    /// </summary>
    public sealed class MoexLoadBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MoexLoadTaskReader _taskReader;
        private readonly ILogger<MoexLoadBackgroundService> _logger;
        private readonly TimeSpan _pollInterval;
        private readonly int _concurrency;

        public MoexLoadBackgroundService(
            IServiceScopeFactory scopeFactory,
            MoexLoadTaskReader taskReader,
            ILogger<MoexLoadBackgroundService> logger,
            TimeSpan pollInterval,
            int concurrency)
        {
            if (concurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(concurrency), "Число дорожек должно быть положительным.");

            _scopeFactory = scopeFactory;
            _taskReader = taskReader;
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
            // Захват до создания области: пока очередь пуста, дорожка не создаёт ни области,
            // ни её служб. Читатель — одиночка без состояния, области ему не требуется.
            MoexLoadTask? task = await _taskReader.ClaimNextPendingTaskAsync(ct);
            if (task is null)
                return false;

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            LoadRunner runner = scope.ServiceProvider.GetRequiredService<LoadRunner>();
            try
            {
                await runner.RunClaimedAsync(task, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Остановка посреди загрузки. RunAsync уже вернул задачу в очередь (pending).
                // Пробрасываем, чтобы внешний цикл штатно завершился.
                throw;
            }
            catch (Exception)
            {
                // Сбой загрузки. RunAsync уже пометил задачу error и записал причину.
                // Исполнитель не падает — берёт следующую.
                MoexLoadTaskLogMessages.BackgroundTaskFailed(_logger, task.Id);
            }

            return true;
        }
    }
}
