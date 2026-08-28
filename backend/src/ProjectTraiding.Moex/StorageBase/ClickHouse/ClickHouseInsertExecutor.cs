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
    /// в формате RowBinary. Общий механизм для всех рядов —
    /// таблица, столбцы, типы и строки приходят параметрами.
    /// Содержимое строк не проверяет: парсер уже признал данные корректными.
    /// </summary>
    public sealed class ClickHouseInsertExecutor
    {
        // Значения настроек имеют тип объекта: числовые литералы упаковывались бы в кучу
        // на каждую вставку. Значение постоянно, поэтому упаковано один раз.
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
        /// </summary>
        public async Task<long> InsertAsync(
            string table,
            IReadOnlyList<string> columns,
            IReadOnlyDictionary<string, string> columnTypes,
            IReadOnlyList<object?[]> rows,
            StorageInsertContext insertContext,
            CancellationToken ct)
        {
            if (rows.Count == 0)
                return 0;

            ClickHouseWriterLogMessages.WriteStarted(_logger, table, rows.Count);

            // ColumnTypes задан явно → драйвер не шлёт SELECT ... WHERE 1=0 перед вставкой.
            InsertOptions options = new InsertOptions
            {
                BatchSize = rows.Count,
                MaxDegreeOfParallelism = 1,
                ColumnTypes = columnTypes,
                // Историческая загрузка шлёт крупные пачки — синхронная вставка кладёт их одной
                // частью. Приём шлёт мелкие частые пачки: без буферизации каждая стала бы
                // отдельной частью. Ожидание подтверждения оставлено включённым: без него
                // вызывающий получил бы успех до фактической записи, и счётчик вставленных
                // строк перестал бы быть правдой.
                CustomSettings = insertContext.Flow == MoexFlows.History
                    ? new Dictionary<string, object>
                    {
                        ["async_insert"] = DisabledSetting,
                    }
                    : new Dictionary<string, object>
                    {
                        ["async_insert"] = EnabledSetting,
                        ["wait_for_async_insert"] = EnabledSetting,
                    },
            };

            // Собственного отрезка трассы у вставки нет намеренно: он создавался бы на
            // каждый инструмент с новыми строками и на каждую страницу загрузки, а исход
            // вставки и число строк несут счётчики, отказ — событие журнала.
            try
            {
                long inserted = await _client.InsertBinaryAsync(table, columns, rows, options, ct);

                RecordInsert(in insertContext, MoexOutcomes.Success, inserted);
                return inserted;
            }
            catch (Exception ex)
            {
                ClickHouseWriterLogMessages.WriteFailed(_logger, ex, table, ex.GetType().Name);

                // Обработчик остаётся один: разводить отмену в отдельный catch значило бы
                // лишить её существующего события журнала. Различие видно по типу исключения,
                // и на исход метрики оно влияет, а на журналирование — нет.
                string outcome = ex is OperationCanceledException
                    ? MoexOutcomes.Cancelled
                    : MoexOutcomes.Error;

                // Число вставленных строк не записывается: драйвер его не подтвердил.
                RecordInsert(in insertContext, outcome, null);

                throw;
            }
        }

        private static void RecordInsert(
            in StorageInsertContext context,
            string outcome,
            long? insertedRows)
        {
            TagList operationTags = new TagList
            {
                { MoexTelemetryAttributes.DataKind, context.DataKind },
                { MoexTelemetryAttributes.Market, context.Market },
                { MoexTelemetryAttributes.Flow, context.Flow },
                { MoexTelemetryAttributes.Outcome, outcome },
            };

            MoexMetrics.StorageInsertOperations.Add(1, operationTags);

            // Число строк записывается только при подтверждении драйвером, а подтверждает
            // он его лишь при успехе. Поэтому у счётчика строк метка исхода постоянна;
            // различие исходов несёт счётчик операций строкой выше.
            if (insertedRows is long rows)
                MoexMetrics.StorageInsertRows.Add(rows, operationTags);
        }
    }
}
