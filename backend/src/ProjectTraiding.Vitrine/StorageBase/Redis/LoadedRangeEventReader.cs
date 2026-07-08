using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Работник чтения потока изменения диапазонов витриной. Владеет именем группы
    /// потребителей «vitrine» в потоке loaded-ranges:changed. Устройство дословно
    /// повторяет TariffEventReader: создать группу, забрать новые записи, подтвердить.
    /// Код инструмента из записи разбирает слушатель, не читатель.
    /// </summary>
    public sealed class LoadedRangeEventReader
    {
        private const string StreamKey = "loaded-ranges:changed";
        private const string GroupName = "vitrine";
        private const string ConsumerName = "vitrine-1";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<LoadedRangeEventReader> _logger;

        public LoadedRangeEventReader(IConnectionMultiplexer redis, ILogger<LoadedRangeEventReader> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task EnsureGroupAsync()
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                await db.StreamCreateConsumerGroupAsync(StreamKey, GroupName, position: "$", createStream: true);
                VitrineLoadedRangeReaderLogMessages.GroupEnsured(_logger, StreamKey, GroupName);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                VitrineLoadedRangeReaderLogMessages.GroupAlreadyExists(_logger, StreamKey, GroupName);
            }
        }

        public async Task<StreamEntry[]> ReadNewAsync(int count)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.StreamReadGroupAsync(
                StreamKey, GroupName, ConsumerName, position: StreamPosition.NewMessages, count: count);
        }

        public async Task AcknowledgeAsync(RedisValue[] ids)
        {
            IDatabase db = _redis.GetDatabase();
            await db.StreamAcknowledgeAsync(StreamKey, GroupName, ids);
        }
    }
}
