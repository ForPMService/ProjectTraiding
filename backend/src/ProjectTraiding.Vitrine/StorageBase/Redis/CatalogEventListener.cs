using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Слушатель изменений справочника: одна пачка сбрасывает справочник и оба карточных кеша.
    /// </summary>
    public sealed class CatalogEventListener : VitrineStreamListener
    {
        public CatalogEventListener(
            CatalogEventReader reader,
            IServiceScopeFactory scopeFactory,
            ILogger<CatalogEventListener> logger,
            TimeSpan pollInterval)
            : base(reader, scopeFactory, logger, pollInterval)
        {
        }

        protected override async Task HandleBatchAsync(
            StreamEntry[] entries,
            IServiceScopeFactory scopeFactory,
            CancellationToken ct)
        {
            // Несколько событий приводят к одному сбросу кешей — схлопывание.
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            InstrumentCatalogCache catalogCache =
                scope.ServiceProvider.GetRequiredService<InstrumentCatalogCache>();
            await catalogCache.InvalidateAsync();

            // Карточки меняются вместе со справочником, поэтому все три кеша
            // обновляются одним событием catalog:changed.
            StockCardCache stockCardCache =
                scope.ServiceProvider.GetRequiredService<StockCardCache>();
            await stockCardCache.InvalidateAsync();
            FuturesCardCache futuresCardCache =
                scope.ServiceProvider.GetRequiredService<FuturesCardCache>();
            await futuresCardCache.InvalidateAsync();
        }
    }
}
