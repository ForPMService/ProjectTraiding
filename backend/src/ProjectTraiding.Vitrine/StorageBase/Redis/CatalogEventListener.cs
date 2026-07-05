using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Фоновый слушатель потока изменения справочника. Постоянно опрашивает поток
    /// через группу потребителей и при появлении события сбрасывает кеш справочника,
    /// после чего ближайшее чтение перечитает свежие данные из базы истины.
    /// Периодический опрос с коротким сном (без блокирующего ожидания).
    /// Кеширующий читатель временный — берётся через область видимости на итерацию.
    /// </summary>
    public sealed class CatalogEventListener : BackgroundService
    {
        private const int MaxBatch = 50;

        private readonly CatalogEventReader _reader;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CatalogEventListener> _logger;
        private readonly TimeSpan _pollInterval;

        public CatalogEventListener(
            CatalogEventReader reader,
            IServiceScopeFactory scopeFactory,
            ILogger<CatalogEventListener> logger,
            TimeSpan pollInterval)
        {
            _reader = reader;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            VitrineCatalogReaderLogMessages.ListenerStarted(_logger, _pollInterval);

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
                    VitrineCatalogReaderLogMessages.PollFailed(_logger, ex, ex.GetType().Name);
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

            VitrineCatalogReaderLogMessages.ListenerStopped(_logger);
        }

        // true — были события и кеш сброшен; false — новых событий нет.
        private async Task<bool> PollOnceAsync(CancellationToken ct)
        {
            StreamEntry[] entries = await _reader.ReadNewAsync(MaxBatch);
            if (entries.Length == 0)
                return false;

            VitrineCatalogReaderLogMessages.EventsReceived(_logger, "catalog:changed", entries.Length);

            // Несколько событий приводят к одному сбросу кеша — схлопывание.
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            InstrumentCatalogCache cache = scope.ServiceProvider.GetRequiredService<InstrumentCatalogCache>();
            await cache.InvalidateAsync();

            // Подтверждаем разбор всех полученных записей.
            RedisValue[] ids = new RedisValue[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                ids[i] = entries[i].Id;
            await _reader.AcknowledgeAsync(ids);

            return true;
        }

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
                    VitrineCatalogReaderLogMessages.PollFailed(_logger, ex, ex.GetType().Name);
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
