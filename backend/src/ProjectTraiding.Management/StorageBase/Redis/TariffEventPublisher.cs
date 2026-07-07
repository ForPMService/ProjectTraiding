using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Redis
{
    /// <summary>
    /// Публикатор события изменения тарифов брокеров.
    /// Единственный владелец записи потока tariffs:changed — контур команд.
    /// После успешной записи тарифа в базу истину добавляет в поток одну краткую запись
    /// о факте изменения. Событие несёт только факт, без данных.
    /// Неудача публикации не роняет запись тарифа: тариф уже записан в базу истину.
    /// </summary>
    public sealed class TariffEventPublisher
    {
        private const string StreamKey = "tariffs:changed";
        private const string EventField = "event";
        private const string EventValue = "changed";

        // Предел длины потока: история сигналов не нужна, храним лишь последние немногие.
        private const int MaxStreamLength = 100;

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<TariffEventPublisher> _logger;

        public TariffEventPublisher(IConnectionMultiplexer redis, ILogger<TariffEventPublisher> logger)
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

                ManagementTariffEventLogMessages.TariffEventPublished(_logger, StreamKey, id.ToString());
            }
            catch (Exception ex)
            {
                // Публикация некритична: тариф уже записан в базе истины.
                ManagementTariffEventLogMessages.TariffEventPublishFailed(_logger, ex, StreamKey, ex.GetType().Name);
            }
        }
    }
}
