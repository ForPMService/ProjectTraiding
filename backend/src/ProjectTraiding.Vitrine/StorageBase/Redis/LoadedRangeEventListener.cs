using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Слушатель изменений диапазонов: каждая пачка адресно сбрасывает ключ каждого
    /// упомянутого инструмента один раз.
    /// </summary>
    public sealed class LoadedRangeEventListener : VitrineStreamListener
    {
        private const string SecidField = "secid";

        public LoadedRangeEventListener(
            LoadedRangeEventReader reader,
            IServiceScopeFactory scopeFactory,
            ILogger<LoadedRangeEventListener> logger,
            TimeSpan pollInterval)
            : base(reader, scopeFactory, logger, pollInterval)
        {
        }

        protected override async Task HandleBatchAsync(
            StreamEntry[] entries,
            IServiceScopeFactory scopeFactory,
            CancellationToken ct)
        {
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
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                LoadedRangeCache cache =
                    scope.ServiceProvider.GetRequiredService<LoadedRangeCache>();
                foreach (string secid in secids)
                    await cache.InvalidateAsync(secid);
            }
        }
    }
}
