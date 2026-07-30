using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>Слушатель изменений тарифов: одна пачка сбрасывает кеш тарифов.</summary>
    public sealed class TariffEventListener : VitrineStreamListener
    {
        public TariffEventListener(
            TariffEventReader reader,
            IServiceScopeFactory scopeFactory,
            ILogger<TariffEventListener> logger,
            TimeSpan pollInterval)
            : base(reader, scopeFactory, logger, pollInterval)
        {
        }

        protected override async Task HandleBatchAsync(
            StreamEntry[] entries,
            IServiceScopeFactory scopeFactory,
            CancellationToken ct)
        {
            // Несколько событий приводят к одному сбросу кеша — схлопывание.
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            BrokerTariffCache cache =
                scope.ServiceProvider.GetRequiredService<BrokerTariffCache>();
            await cache.InvalidateAsync();
        }
    }
}
