using ClickHouse.Driver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Исполнитель одной вставки в ClickHouse: пишет готовую пачку строк одним INSERT
    /// в формате RowBinary с токеном дедупликации. Общий механизм для всех рядов —
    /// таблица, столбцы, типы, строки и токен приходят параметрами.
    /// Содержимое строк не проверяет: парсер уже признал данные корректными.
    /// </summary>
    public sealed class ClickHouseInsertExecutor
    {
        private readonly ClickHouseClient _client;
        private readonly ILogger<ClickHouseInsertExecutor> _logger;

        public ClickHouseInsertExecutor(ClickHouseClient client, ILogger<ClickHouseInsertExecutor> logger)
        {
            _client = client;
            _logger = logger;
        }

        /// <summary>
        /// Пишет одну пачку одним INSERT. Возвращает число вставленных строк.
        /// deduplicationToken уникален для пачки: повтор той же пачки тем же токеном отсекается.
        /// </summary>
        public async Task<long> InsertAsync(
            string table,
            IReadOnlyList<string> columns,
            IReadOnlyDictionary<string, string> columnTypes,
            IReadOnlyList<object?[]> rows,
            string deduplicationToken,
            CancellationToken ct)
        {
            if (rows.Count == 0)
                return 0;

            ClickHouseWriterLogMessages.WriteStarted(_logger, table, rows.Count);
            long startTs = Stopwatch.GetTimestamp();

            // BatchSize = размер пачки + параллелизм 1 → ровно один INSERT = один токен.
            // ColumnTypes задан явно → драйвер не шлёт SELECT ... WHERE 1=0 перед вставкой.
            InsertOptions options = new InsertOptions
            {
                BatchSize = rows.Count,
                MaxDegreeOfParallelism = 1,
                ColumnTypes = columnTypes,
                CustomSettings = new Dictionary<string, object>
                {
                    ["async_insert"] = 0,
                    ["insert_deduplicate"] = 1,
                    ["insert_deduplication_token"] = deduplicationToken,
                },
            };

            try
            {
                long inserted = await _client.InsertBinaryAsync(table, columns, rows, options, ct);

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ClickHouseWriterLogMessages.WriteCompleted(_logger, table, inserted, elapsed);
                return inserted;
            }
            catch (Exception ex)
            {
                ClickHouseWriterLogMessages.WriteFailed(_logger, ex, table, ex.GetType().Name);
                throw;
            }
        }
    }
}
