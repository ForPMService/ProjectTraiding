using ProjectTraiding.Moex.Contracts.Dto.Operations;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Loading;
using StackExchange.Redis;
using System;
using System.Globalization;
using System.Text.Json;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Писатель живого прогресса задачи в оперативное хранилище. Единственный владелец записи
    /// ключей прогресса задач — контур биржи. Реализует приёмник хода загрузки:
    /// на каждом сбросе пачки кладёт значение прогресса под ключ задачи со сроком жизни.
    /// Срок жизни — самоочистка: прогресс завершённой задачи не лежит в памяти вечно.
    /// Сбой хранилища не роняет загрузку: пишется в журнал и проглатывается.
    /// </summary>
    public sealed class LoadProgressWriter : ILoadProgressReporter
    {
        private const string KeyPrefix = "load:task:progress:";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<LoadProgressWriter> _logger;
        private readonly TimeSpan _ttl;

        public LoadProgressWriter(
            IConnectionMultiplexer redis,
            ILogger<LoadProgressWriter> logger,
            TimeSpan ttl)
        {
            _redis = redis;
            _logger = logger;
            _ttl = ttl;
        }

        public async Task ReportAsync(Guid taskId, long rowsRead, DateTime lastSourceTime, CancellationToken ct)
        {
            string key = KeyPrefix + taskId.ToString("N", CultureInfo.InvariantCulture);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            try
            {
                IDatabase db = _redis.GetDatabase();

                DateTimeOffset startedAt = now;
                RedisValue existing = await db.StringGetAsync(key);
                if (!existing.IsNullOrEmpty)
                {
                    LoadProgressValue? prev = JsonSerializer.Deserialize(
                        (string)existing!, AppJsonContext.Default.LoadProgressValue);
                    if (prev is not null)
                        startedAt = prev.StartedAt;
                }

                LoadProgressValue value = new LoadProgressValue(
                    RowsRead: rowsRead,
                    LastSourceTime: new DateTimeOffset(DateTime.SpecifyKind(lastSourceTime, DateTimeKind.Utc)),
                    StartedAt: startedAt,
                    UpdatedAt: now,
                    ReceivedAt: now,
                    IsStale: false);
                string json = JsonSerializer.Serialize(value, AppJsonContext.Default.LoadProgressValue);
                await db.StringSetAsync(key, json, _ttl);
                MoexLoadProgressLogMessages.ProgressWritten(_logger, key, rowsRead);
            }
            catch (Exception ex)
            {
                MoexLoadProgressLogMessages.ProgressWriteFailed(_logger, ex, key, ex.GetType().Name);
            }
        }
    }
}
