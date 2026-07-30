using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Общая техническая механика чтения Redis-потока витриной: подготовка группы,
    /// чтение новых записей и подтверждение их разбора. Предметный тип владеет только ключом.
    /// </summary>
    public abstract class VitrineStreamReader
    {
        private const string GroupName = "vitrine";
        private const string ConsumerName = "vitrine-1";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger _logger;

        /// <summary>Ключ потока; слушатель использует тот же источник истины для журналов.</summary>
        internal string StreamKey { get; }

        protected VitrineStreamReader(
            IConnectionMultiplexer redis,
            ILogger logger,
            string streamKey)
        {
            _redis = redis;
            _logger = logger;
            StreamKey = streamKey;
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
                // "$" — созданная группа начинает с событий, появившихся после её создания.
                // Дальнейшее чтение идёт только по StreamPosition.NewMessages.
                // createStream: true — если потока ещё нет, он создаётся вместе с группой.
                await db.StreamCreateConsumerGroupAsync(
                    StreamKey,
                    GroupName,
                    position: "$",
                    createStream: true);
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
                StreamKey,
                GroupName,
                ConsumerName,
                position: StreamPosition.NewMessages,
                count: count);
        }

        /// <summary>Подтвердить разбор записей — убрать их из перечня взятого в работу.</summary>
        public async Task AcknowledgeAsync(RedisValue[] ids)
        {
            IDatabase db = _redis.GetDatabase();
            await db.StreamAcknowledgeAsync(StreamKey, GroupName, ids);
        }
    }
}
