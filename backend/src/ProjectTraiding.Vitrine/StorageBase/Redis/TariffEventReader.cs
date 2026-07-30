using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Работник чтения потока изменения тарифов витриной. Владеет именем группы
    /// потребителей «vitrine» в потоке tariffs:changed. Устройство дословно повторяет
    /// CatalogEventReader: создать группу, забрать новые записи, подтвердить разбор.
    /// </summary>
    public sealed class TariffEventReader
    {
        private const string StreamKey = "tariffs:changed";
        private const string GroupName = "vitrine";
        private const string ConsumerName = "vitrine-1";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<TariffEventReader> _logger;

        public TariffEventReader(IConnectionMultiplexer redis, ILogger<TariffEventReader> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        /// <summary>
        /// Однократная подготовка: создать группу, а заодно и поток, если их ещё нет.
        /// Повторный вызов при уже существующей группе безвреден и молча пропускается.
        /// </summary>
        public async Task EnsureGroupAsync()
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                // createStream: true — если потока ещё нет, он будет создан заодно с группой.
                // Позиция "0" не важна: событие несёт лишь факт, читаем только новые записи.
                await db.StreamCreateConsumerGroupAsync(StreamKey, GroupName, position: "$", createStream: true);
                VitrineStreamLogMessages.GroupEnsured(_logger, StreamKey, GroupName);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // Группа уже существует — это не ошибка, а обычное состояние после первого запуска.
                VitrineStreamLogMessages.GroupAlreadyExists(_logger, StreamKey, GroupName);
            }
        }

        /// <summary>
        /// Забрать новые, ещё не разобранные записи для группы. Пустой массив — новых нет.
        /// </summary>
        public async Task<StreamEntry[]> ReadNewAsync(int count)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.StreamReadGroupAsync(
                StreamKey, GroupName, ConsumerName, position: StreamPosition.NewMessages, count: count);
        }

        /// <summary>
        /// Подтвердить разбор записей — убрать их из перечня взятого в работу.
        /// </summary>
        public async Task AcknowledgeAsync(RedisValue[] ids)
        {
            IDatabase db = _redis.GetDatabase();
            await db.StreamAcknowledgeAsync(StreamKey, GroupName, ids);
        }
    }
}
