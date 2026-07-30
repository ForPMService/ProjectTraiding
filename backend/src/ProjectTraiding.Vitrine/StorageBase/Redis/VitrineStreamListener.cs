using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Общий жизненный цикл слушателя Redis-потока: подготовка группы, опрос,
    /// обработка недоступности хранилища и подтверждение разобранной пачки.
    /// </summary>
    public abstract class VitrineStreamListener : BackgroundService
    {
        private const int MaxBatch = 50;

        private readonly VitrineStreamReader _reader;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly TimeSpan _pollInterval;

        protected VitrineStreamListener(
            VitrineStreamReader reader,
            IServiceScopeFactory scopeFactory,
            ILogger logger,
            TimeSpan pollInterval)
        {
            _reader = reader;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            VitrineStreamLogMessages.ListenerStarted(
                _logger,
                _reader.StreamKey,
                _pollInterval);

            // Однократная подготовка группы. При недоступности хранилища повторяем,
            // не роняя службу: без группы читать нечего.
            await EnsureGroupWithRetryAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool worked = false;
                try
                {
                    worked = await PollOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // штатная остановка хоста — не ошибка
                }
                catch (Exception ex)
                {
                    // Сбой опроса (например, хранилище недоступно) не валит службу.
                    VitrineStreamLogMessages.PollFailed(
                        _logger,
                        ex,
                        _reader.StreamKey,
                        ex.GetType().Name);
                }

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

            VitrineStreamLogMessages.ListenerStopped(_logger, _reader.StreamKey);
        }

        // true — были события и предметное действие выполнено; false — новых событий нет.
        private async Task<bool> PollOnceAsync(CancellationToken ct)
        {
            StreamEntry[] entries = await _reader.ReadNewAsync(MaxBatch);
            if (entries.Length == 0)
                return false;

            VitrineStreamLogMessages.EventsReceived(
                _logger,
                _reader.StreamKey,
                entries.Length);

            await HandleBatchAsync(entries, _scopeFactory, ct);

            // Подтверждаем все записи пачки, включая те, чьи предметные поля не разобраны.
            RedisValue[] ids = new RedisValue[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                ids[i] = entries[i].Id;
            await _reader.AcknowledgeAsync(ids);

            return true;
        }

        /// <summary>
        /// Предметное действие над полученной пачкой: какие кеши сбросить и по каким ключам.
        /// Область зависимостей создаёт наследник — там и тогда, где она действительно нужна.
        /// Подтверждение разбора выполняет каркас после возврата, по всем записям пачки.
        /// </summary>
        protected abstract Task HandleBatchAsync(
            StreamEntry[] entries,
            IServiceScopeFactory scopeFactory,
            CancellationToken ct);

        private async Task EnsureGroupWithRetryAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _reader.EnsureGroupAsync();
                    return;
                }
                catch (Exception ex)
                {
                    VitrineStreamLogMessages.PollFailed(
                        _logger,
                        ex,
                        _reader.StreamKey,
                        ex.GetType().Name);
                    try
                    {
                        await Task.Delay(_pollInterval, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }
    }
}
