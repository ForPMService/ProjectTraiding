using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Management.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    /// <summary>
    /// Создание задачи загрузки (moex_load_tasks) по команде оператора.
    /// Статус и версии не задаются — берутся DEFAULT схемы. id генерирует база (uuidv7()),
    /// возвращаем его через RETURNING. Возврат — Guid, а не DbWriteResult: идентификатор
    /// задачи это uuid, а общий DbWriteResult.Id рассчитан на bigint-таблицы.
    /// </summary>
    public sealed class LoadTaskWriter
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<LoadTaskWriter> _logger;

        public LoadTaskWriter(NpgsqlDataSource dataSource, ILogger<LoadTaskWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<Guid> CreateAsync(LoadTaskCreateRequest request, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_load_tasks
                    (secid, market, boardid, data_kind, candle_interval,
                     date_from, date_till, storage_target)
                VALUES
                    (@secid, @market, @boardid, @data_kind, @candle_interval,
                     @date_from, @date_till, @storage_target)
                RETURNING id
                """, connection);

            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = request.Secid;
            cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = request.Market;
            cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = request.Boardid;
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = request.DataKind;
            cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                (object?)request.CandleInterval ?? DBNull.Value;
            cmd.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = request.DateFrom;
            cmd.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = request.DateTill;
            cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = request.StorageTarget;

            object? idObj = await cmd.ExecuteScalarAsync(ct);
            return (Guid)idObj!;
        }

        public async Task<BulkCreateResult> CreateManyAsync(
            IReadOnlyList<LoadTaskCreateRequest> tasks,
            CancellationToken ct)
        {
            if (tasks.Count == 0)
                return new BulkCreateResult(
                    ExpandedCount: 0,
                    InsertedCount: 0,
                    SkippedDuplicateCount: 0);

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            try
            {
                await using NpgsqlCommand createTempCommand = new NpgsqlCommand("""
                    CREATE TEMP TABLE tmp_moex_load_tasks_bulk (
                        secid           text,
                        market          text,
                        boardid         text,
                        data_kind       text,
                        candle_interval integer,
                        date_from       date,
                        date_till       date,
                        storage_target  text
                    ) ON COMMIT DROP
                    """, connection, transaction);
                await createTempCommand.ExecuteNonQueryAsync(ct);

                // COPY держит соединение в состоянии Copy до Dispose импортёра. Ограничиваем его
                // жизнь собственным блоком: после CompleteAsync импортёр освобождается здесь же,
                // и соединение выходит из Copy ДО следующей команды. Иначе INSERT на том же
                // соединении падает с NpgsqlOperationInProgressException (state 'Copy').
                // CompleteAsync — внутри блока: без него Dispose истолковал бы импорт как отмену
                // и откатил бы COPY.
                await using (NpgsqlBinaryImporter importer = await connection.BeginBinaryImportAsync("""
                    COPY tmp_moex_load_tasks_bulk
                        (secid, market, boardid, data_kind, candle_interval,
                         date_from, date_till, storage_target)
                    FROM STDIN (FORMAT BINARY)
                    """, ct))
                {
                    for (int i = 0; i < tasks.Count; i++)
                    {
                        LoadTaskCreateRequest task = tasks[i];

                        await importer.StartRowAsync(ct);
                        await importer.WriteAsync(task.Secid, NpgsqlDbType.Text, ct);
                        await importer.WriteAsync(task.Market, NpgsqlDbType.Text, ct);
                        await importer.WriteAsync(task.Boardid, NpgsqlDbType.Text, ct);
                        await importer.WriteAsync(task.DataKind, NpgsqlDbType.Text, ct);

                        if (task.CandleInterval is int candleInterval)
                            await importer.WriteAsync(candleInterval, NpgsqlDbType.Integer, ct);
                        else
                            await importer.WriteNullAsync(ct);

                        await importer.WriteAsync(task.DateFrom, NpgsqlDbType.Date, ct);
                        await importer.WriteAsync(task.DateTill, NpgsqlDbType.Date, ct);
                        await importer.WriteAsync(task.StorageTarget, NpgsqlDbType.Text, ct);
                    }

                    await importer.CompleteAsync(ct);
                }

                await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
                    INSERT INTO moex_load_tasks
                        (secid, market, boardid, data_kind, candle_interval,
                         date_from, date_till, storage_target)
                    SELECT secid, market, boardid, data_kind, candle_interval,
                           date_from, date_till, storage_target
                    FROM tmp_moex_load_tasks_bulk
                    ON CONFLICT (secid, market, boardid, data_kind, candle_interval,
                                 date_from, date_till, storage_target)
                        WHERE status IN ('pending', 'running', 'partial')
                    DO NOTHING
                    """, connection, transaction);
                int insertedCount = await insertCommand.ExecuteNonQueryAsync(ct);

                await transaction.CommitAsync(ct);

                return new BulkCreateResult(
                    ExpandedCount: tasks.Count,
                    InsertedCount: insertedCount,
                    SkippedDuplicateCount: tasks.Count - insertedCount);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(
                    _logger,
                    ex,
                    "moex_load_tasks",
                    ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
