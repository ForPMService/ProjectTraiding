using ClickHouse.Driver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ProjectTraiding.Moex.Infrastructure.Telemetry;

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
        // Значения настроек имеют тип объекта: числовые литералы упаковывались бы в кучу
        // на каждую вставку. Оба значения постоянны, поэтому упакованы один раз.
        private static readonly object DisabledSetting = 0;
        private static readonly object EnabledSetting = 1;

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
            StorageInsertContext insertContext,
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
                    ["async_insert"] = DisabledSetting,
                    ["insert_deduplicate"] = EnabledSetting,
                    ["insert_deduplication_token"] = deduplicationToken,
                },
            };

            using Activity? insertActivity =
                MoexTelemetry.ActivitySource.StartActivity("storage.clickhouse.insert");
            insertActivity?.SetTag(MoexTelemetryAttributes.DataKind, insertContext.DataKind);
            insertActivity?.SetTag(MoexTelemetryAttributes.Market, insertContext.Market);
            insertActivity?.SetTag(MoexTelemetryAttributes.Flow, insertContext.Flow);

            try
            {
                long inserted = await _client.InsertBinaryAsync(table, columns, rows, options, ct);

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);

                RecordInsert(in insertContext, MoexOutcomes.Success, inserted, elapsed);
                insertActivity?.SetStatus(ActivityStatusCode.Ok);
                return inserted;
            }
            catch (Exception ex)
            {
                insertActivity?.SetStatus(
                    ex is OperationCanceledException
                        ? ActivityStatusCode.Ok
                        : ActivityStatusCode.Error,
                    ex is OperationCanceledException ? null : ex.Message);
                ClickHouseWriterLogMessages.WriteFailed(_logger, ex, table, ex.GetType().Name);

                // Обработчик остаётся один: разводить отмену в отдельный catch значило бы
                // лишить её существующего события журнала. Различие видно по типу исключения,
                // и на исход метрики оно влияет, а на журналирование — нет.
                string outcome = ex is OperationCanceledException
                    ? MoexOutcomes.Cancelled
                    : MoexOutcomes.Error;

                // Число вставленных строк не записывается: драйвер его не подтвердил.
                RecordInsert(in insertContext, outcome, null, Stopwatch.GetElapsedTime(startTs));

                throw;
            }
        }

        private static void RecordInsert(
            in StorageInsertContext context,
            string outcome,
            long? insertedRows,
            TimeSpan elapsed)
        {
            TagList operationTags = new TagList
            {
                { MoexTelemetryAttributes.DataKind, context.DataKind },
                { MoexTelemetryAttributes.Market, context.Market },
                { MoexTelemetryAttributes.Flow, context.Flow },
                { MoexTelemetryAttributes.Outcome, outcome },
            };

            MoexMetrics.StorageInsertOperations.Add(1, operationTags);

            if (insertedRows is long rows)
                MoexMetrics.StorageInsertRows.Add(rows, operationTags);

            TagList durationTags = new TagList
            {
                { MoexTelemetryAttributes.DataKind, context.DataKind },
                { MoexTelemetryAttributes.Market, context.Market },
                { MoexTelemetryAttributes.Flow, context.Flow },
            };

            MoexMetrics.StorageInsertDuration.Record(elapsed.TotalSeconds, durationTags);
        }
    }
}
