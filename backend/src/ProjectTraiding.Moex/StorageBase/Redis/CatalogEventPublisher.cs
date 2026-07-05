using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Публикатор события изменения справочника инструментов.
    /// Единственный владелец записи потока catalog:changed — контур биржи.
    /// После успешного обновления справочника в базе истины добавляет в поток одну
    /// краткую запись о факте изменения. Событие несёт только факт, без данных.
    /// Неудача публикации не роняет обновление справочника: база уже обновлена,
    /// а суточный срок жизни ключа кеша служит страховкой на случай потерянного события.
    /// </summary>
    public sealed class CatalogEventPublisher
    {
        private const string StreamKey = "catalog:changed";
        private const string EventField = "event";
        private const string EventValue = "changed";

        // Предел длины потока: история сигналов не нужна, храним лишь последние немногие.
        private const int MaxStreamLength = 100;

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<CatalogEventPublisher> _logger;

        public CatalogEventPublisher(IConnectionMultiplexer redis, ILogger<CatalogEventPublisher> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task PublishChangedAsync()
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                // Приблизительная обрезка по длине дешевле для хранилища, чем точная.
                RedisValue id = await db.StreamAddAsync(
                    StreamKey,
                    EventField,
                    EventValue,
                    messageId: null,
                    maxLength: MaxStreamLength,
                    useApproximateMaxLength: true);

                MoexCatalogEventLogMessages.CatalogEventPublished(_logger, StreamKey, id.ToString());
            }
            catch (Exception ex)
            {
                // Публикация некритична: справочник уже обновлён в базе истины.
                MoexCatalogEventLogMessages.CatalogEventPublishFailed(_logger, ex, StreamKey, ex.GetType().Name);
            }
        }
    }
}
