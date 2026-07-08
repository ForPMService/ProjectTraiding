using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Публикатор события изменения диапазонов загрузки по инструменту.
    /// Единственный владелец записи потока loaded-ranges:changed — контур биржи.
    /// После успешной записи диапазона в базу истину кладёт в поток запись с кодом
    /// инструмента, чтобы витрина сбросила адресный ключ именно этого инструмента.
    /// Неудача публикации не роняет загрузку: диапазон уже записан, а суточный срок
    /// жизни ключа кеша служит страховкой на случай потерянного события.
    /// </summary>
    public sealed class LoadedRangeEventPublisher
    {
        private const string StreamKey = "loaded-ranges:changed";
        private const string EventField = "event";
        private const string EventValue = "changed";
        private const string SecidField = "secid";

        private const int MaxStreamLength = 1000;

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<LoadedRangeEventPublisher> _logger;

        public LoadedRangeEventPublisher(IConnectionMultiplexer redis, ILogger<LoadedRangeEventPublisher> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task PublishChangedAsync(string secid)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                NameValueEntry[] fields = new NameValueEntry[]
                {
                    new NameValueEntry(EventField, EventValue),
                    new NameValueEntry(SecidField, secid),
                };
                RedisValue id = await db.StreamAddAsync(
                    StreamKey,
                    fields,
                    messageId: null,
                    maxLength: MaxStreamLength,
                    useApproximateMaxLength: true);

                MoexLoadedRangeEventLogMessages.EventPublished(_logger, StreamKey, secid, id.ToString());
            }
            catch (Exception ex)
            {
                MoexLoadedRangeEventLogMessages.EventPublishFailed(_logger, ex, StreamKey, ex.GetType().Name);
            }
        }
    }
}
