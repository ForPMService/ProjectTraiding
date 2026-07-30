using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Фоновый слушатель потока изменения диапазонов. Каркас повторяет TariffEventListener.
    /// Отличие: сброс адресный. Из пачки событий собираются коды инструментов, и по
    /// каждому уникальному коду ключ его диапазонов сбрасывается один раз (схлопывание).
    /// </summary>
    public sealed class LoadedRangeEventListener : BackgroundService
    {
        private const int MaxBatch = 50;
        private const string SecidField = "secid";

        private readonly LoadedRangeEventReader _reader;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LoadedRangeEventListener> _logger;
        private readonly TimeSpan _pollInterval;

        public LoadedRangeEventListener(
            LoadedRangeEventReader reader,
            IServiceScopeFactory scopeFactory,
            ILogger<LoadedRangeEventListener> logger,
            TimeSpan pollInterval)
        {
            _reader = reader;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = pollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            VitrineStreamLogMessages.ListenerStarted(_logger, "loaded-ranges:changed", _pollInterval);

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
                        _logger, ex, "loaded-ranges:changed", ex.GetType().Name);
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

            VitrineStreamLogMessages.ListenerStopped(_logger, "loaded-ranges:changed");
        }

        // true — были события и адресные ключи сброшены; false — новых событий нет.
        private async Task<bool> PollOnceAsync(CancellationToken ct)
        {
            StreamEntry[] entries = await _reader.ReadNewAsync(MaxBatch);
            if (entries.Length == 0)
                return false;

            VitrineStreamLogMessages.EventsReceived(_logger, "loaded-ranges:changed", entries.Length);

            // Схлопывание: собираем уникальные коды инструментов из пачки, каждый ключ
            // сбрасываем один раз, даже если событий по инструменту пришло несколько.
            HashSet<string> secids = new();
            for (int i = 0; i < entries.Length; i++)
            {
                foreach (NameValueEntry field in entries[i].Values)
                {
                    if (field.Name == SecidField)
                    {
                        string secid = field.Value.ToString();
                        if (!string.IsNullOrEmpty(secid))
                            secids.Add(secid);
                        break;
                    }
                }
            }

            if (secids.Count > 0)
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                LoadedRangeCache cache = scope.ServiceProvider.GetRequiredService<LoadedRangeCache>();
                foreach (string secid in secids)
                    await cache.InvalidateAsync(secid);
            }

            // Подтверждаем разбор всех полученных записей (в том числе без кода инструмента).
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
                    VitrineStreamLogMessages.PollFailed(
                        _logger, ex, "loaded-ranges:changed", ex.GetType().Name);
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
