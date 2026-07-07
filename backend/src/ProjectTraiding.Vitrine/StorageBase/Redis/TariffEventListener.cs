using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Фоновый слушатель потока изменения тарифов. Устройство дословно повторяет
    /// CatalogEventListener, но при появлении события сбрасывает кеш тарифов
    /// (BrokerTariffCache.InvalidateAsync), а не кеш справочника.
    /// </summary>
    public sealed class TariffEventListener : BackgroundService
    {
        private const int MaxBatch = 50;

        private readonly TariffEventReader _reader;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TariffEventListener> _logger;
        private readonly TimeSpan _pollInterval;

        public TariffEventListener(
            TariffEventReader reader,
            IServiceScopeFactory scopeFactory,
            ILogger<TariffEventListener> logger,
            TimeSpan pollInterval)
        {
            _reader = reader;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            VitrineTariffReaderLogMessages.ListenerStarted(_logger, _pollInterval);

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
                    VitrineTariffReaderLogMessages.PollFailed(_logger, ex, ex.GetType().Name);
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

            VitrineTariffReaderLogMessages.ListenerStopped(_logger);
        }

        // true — были события и кеш сброшен; false — новых событий нет.
        private async Task<bool> PollOnceAsync(CancellationToken ct)
        {
            StreamEntry[] entries = await _reader.ReadNewAsync(MaxBatch);
            if (entries.Length == 0)
                return false;

            VitrineTariffReaderLogMessages.EventsReceived(_logger, "tariffs:changed", entries.Length);

            // Несколько событий приводят к одному сбросу кеша — схлопывание.
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            BrokerTariffCache cache = scope.ServiceProvider.GetRequiredService<BrokerTariffCache>();
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
                    VitrineTariffReaderLogMessages.PollFailed(_logger, ex, ex.GetType().Name);
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
