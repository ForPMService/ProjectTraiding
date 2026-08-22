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
    /// Создание и операторское снятие заданий загрузки (moex_load_tasks).
    ///
    /// При создании статус и версии не задаются — берутся DEFAULT схемы. id генерирует
    /// база (uuidv7()), возвращаем его через RETURNING. Возврат — Guid, а не общий
    /// ManagementWriteResult: идентификатор задачи это uuid, а поле Id общего типа
    /// рассчитано на таблицы с целочисленным ключом.
    ///
    /// Команды отмены переводят pending и partial в cancelled, а для running фиксируют
    /// запрос остановки. Финальный статус выполняющегося задания устанавливает контур Moex.
    /// </summary>
    public sealed class LoadTaskWriter
    {
        private const string LoadTasksTable = "moex_load_tasks";
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<LoadTaskWriter> _logger;

        public LoadTaskWriter(NpgsqlDataSource dataSource, ILogger<LoadTaskWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        /// <summary>
        /// Ставит одно задание загрузки. Возврат null — по инструменту идёт удаление
        /// данных, задание не создано.
        ///
        /// Условие not exists стоит внутри вставки, а не отдельной проверкой перед
        /// ней: проверка отдельным запросом оставляет окно шириной в промежуток
        /// между двумя запросами, условие внутри запроса — нулевое окно для всех
        /// удалений, зафиксированных до начала этого оператора. Удаление,
        /// зафиксированное уже после начала оператора, не отсекается ни тем ни
        /// другим способом (раздел 5.3 задания).
        /// </summary>
        public async Task<Guid?> CreateAsync(LoadTaskCreateRequest request, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_load_tasks
                    (secid, market, boardid, data_kind, candle_interval,
                     date_from, date_till, storage_target)
                SELECT
                    @secid, @market, @boardid, @data_kind, @candle_interval,
                    @date_from, @date_till, @storage_target
                WHERE NOT EXISTS (
                    SELECT 1 FROM moex_instrument_data_deletions d
                    WHERE d.secid = @secid AND d.status = 'started'
                )
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
            return idObj is Guid id ? id : (Guid?)null;
        }

        public async Task<BulkCreateResult> CreateManyAsync(
            IReadOnlyList<LoadTaskCreateRequest> tasks,
            CancellationToken ct)
        {
            if (tasks.Count == 0)
                return new BulkCreateResult(
                    ExpandedCount: 0,
                    InsertedCount: 0,
                    SkippedDuplicateCount: 0,
                    BlockedSecids: Array.Empty<string>());

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

                // Один оператор — один снимок. Общее выражение blocked собирает
                // инструменты пакета, по которым идёт очистка; вставка выполняется
                // только при пустом blocked и потому либо проходит целиком, либо не
                // вставляет ничего. Разнести это на два оператора нельзя: в режиме
                // READ COMMITTED у них разные снимки, и состояние удаления между ними
                // успевает измениться — строка 'started' переходит в 'finished' при
                // завершении очистки или исчезает вовсе, если координатор снял
                // несостоявшееся задание. Тогда вставка отсекла бы строки по признаку,
                // которого второй запрос уже не видит.
                //
                // ON CONFLICT ... DO NOTHING сохраняется без изменений: пропуск дублей —
                // штатный исход постановки, к удалению отношения не имеющий. RETURNING
                // отдаёт только фактически вставленные строки, поэтому count(*) над ним
                // и есть число созданных заданий.
                //
                // Список заблокированных возвращается склейкой через запятую, а не
                // массивом: коды инструментов Московской биржи запятых не содержат,
                // а склейка не требует сопоставления типа-массива при нативной
                // компиляции.
                await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
                    WITH blocked AS (
                        SELECT DISTINCT t.secid
                        FROM tmp_moex_load_tasks_bulk t
                        WHERE EXISTS (
                            SELECT 1 FROM moex_instrument_data_deletions d
                            WHERE d.secid = t.secid AND d.status = 'started'
                        )
                    ),
                    inserted AS (
                        INSERT INTO moex_load_tasks
                            (secid, market, boardid, data_kind, candle_interval,
                             date_from, date_till, storage_target)
                        SELECT t.secid, t.market, t.boardid, t.data_kind, t.candle_interval,
                               t.date_from, t.date_till, t.storage_target
                        FROM tmp_moex_load_tasks_bulk t
                        WHERE NOT EXISTS (SELECT 1 FROM blocked)
                        ON CONFLICT (secid, market, boardid, data_kind, candle_interval,
                                     date_from, date_till, storage_target)
                            WHERE status IN ('pending', 'running', 'partial')
                        DO NOTHING
                        RETURNING 1
                    )
                    SELECT
                        (SELECT count(*) FROM inserted)                                   AS inserted_count,
                        (SELECT string_agg(b.secid, ',' ORDER BY b.secid) FROM blocked b) AS blocked_secids
                    """, connection, transaction);

                long insertedCount;
                string? blockedRaw;
                await using (NpgsqlDataReader reader = await insertCommand.ExecuteReaderAsync(ct))
                {
                    await reader.ReadAsync(ct);
                    insertedCount = reader.GetInt64(0);
                    blockedRaw = reader.IsDBNull(1) ? null : reader.GetString(1);
                }

                // Отката не требуется даже при блокировке: вставка при непустом blocked
                // не создаёт ни одной строки, и фиксировать нечего кроме удаления
                // временной таблицы, которое произойдёт при завершении транзакции.
                await transaction.CommitAsync(ct);

                if (blockedRaw is not null)
                {
                    string[] parts = blockedRaw.Split(',');
                    List<string> blocked = new List<string>(parts.Length);
                    for (int i = 0; i < parts.Length; i++)
                        blocked.Add(parts[i]);

                    return new BulkCreateResult(
                        ExpandedCount: tasks.Count,
                        InsertedCount: 0,
                        SkippedDuplicateCount: 0,
                        BlockedSecids: blocked);
                }

                return new BulkCreateResult(
                    ExpandedCount: tasks.Count,
                    InsertedCount: (int)insertedCount,
                    SkippedDuplicateCount: tasks.Count - (int)insertedCount,
                    BlockedSecids: Array.Empty<string>());
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

        /// <summary>
        /// Снимает всю ожидающую очередь: pending и partial → cancelled; для running
        /// фиксирует запрос отмены, не меняя статус. Фактическую остановку и финальный
        /// running → cancelled выполняет координатор Moex.
        ///
        /// Статус partial сохранён в схеме и страховочно обрабатывается запросами,
        /// хотя кодом больше не проставляется. Он входит в уникальный индекс активных
        /// заданий, и оставленная строка блокировала бы повторную постановку того же диапазона.
        ///
        /// Статусы done и error не трогаем: это завершённые исходы, а не очередь.
        ///
        /// finished_at заполняется и для заданий, которые никогда не запускались. Поэтому
        /// у снятого ожидающего задания started_at остаётся пустым при заполненном
        /// finished_at — это нормальное сочетание, а не повреждение данных.
        ///
        /// Один UPDATE покрывает оба набора состояний. Если исполнитель захватил строку
        /// конкурентно, повторная проверка условия после ожидания блокировки всё ещё видит
        /// running и ставит запрос. Два отдельных оператора оставили бы окно потери отмены.
        /// Условие cancel_requested_at IS NULL делает повторный вызов идемпотентным.
        /// </summary>
        public async Task<CancelResult> CancelAllAsync(CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, LoadTasksTable);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    WITH affected AS (
                        UPDATE moex_load_tasks
                        SET status = CASE
                                WHEN status IN ('pending', 'partial') THEN 'cancelled'
                                ELSE status END,
                            finished_at = CASE
                                WHEN status IN ('pending', 'partial') THEN now()
                                ELSE finished_at END,
                            stop_reason = CASE
                                WHEN status IN ('pending', 'partial') THEN 'operator_cancelled'
                                ELSE stop_reason END,
                            error_message = CASE
                                WHEN status IN ('pending', 'partial') THEN null
                                ELSE error_message END,
                            cancel_requested_at = CASE
                                WHEN status = 'running' THEN now()
                                ELSE cancel_requested_at END
                        WHERE status IN ('pending', 'partial')
                           OR (status = 'running' AND cancel_requested_at IS NULL)
                        RETURNING status
                    )
                    SELECT
                        count(*) FILTER (WHERE status = 'cancelled') AS cancelled_count,
                        count(*) FILTER (WHERE status = 'running')   AS cancel_requested_count
                    FROM affected
                    """, connection);

                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                int cancelledCount = (int)reader.GetInt64(0);
                int cancelRequestedCount = (int)reader.GetInt64(1);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.LoadTasksCancelledAll(
                    _logger,
                    cancelledCount,
                    cancelRequestedCount,
                    elapsed);
                return new CancelResult(cancelledCount, cancelRequestedCount, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, LoadTasksTable, ex.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// Снимает ожидающие задания одного инструмента и запрашивает остановку его
        /// выполняющихся заданий независимо от рынка, режима торгов, вида данных,
        /// интервала свечей, окна дат и способа постановки. Правила по статусам те же,
        /// что в CancelAllAsync.
        ///
        /// Отдельный метод, а не общий с необязательным secid: не переданный по ошибке
        /// параметр превратил бы точечную команду в снятие всей очереди.
        /// </summary>
        public async Task<CancelResult> CancelInstrumentAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, LoadTasksTable);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    WITH affected AS (
                        UPDATE moex_load_tasks
                        SET status = CASE
                                WHEN status IN ('pending', 'partial') THEN 'cancelled'
                                ELSE status END,
                            finished_at = CASE
                                WHEN status IN ('pending', 'partial') THEN now()
                                ELSE finished_at END,
                            stop_reason = CASE
                                WHEN status IN ('pending', 'partial') THEN 'operator_cancelled'
                                ELSE stop_reason END,
                            error_message = CASE
                                WHEN status IN ('pending', 'partial') THEN null
                                ELSE error_message END,
                            cancel_requested_at = CASE
                                WHEN status = 'running' THEN now()
                                ELSE cancel_requested_at END
                        WHERE secid = @secid
                          AND (
                              status IN ('pending', 'partial')
                              OR (status = 'running' AND cancel_requested_at IS NULL)
                          )
                        RETURNING status
                    )
                    SELECT
                        count(*) FILTER (WHERE status = 'cancelled') AS cancelled_count,
                        count(*) FILTER (WHERE status = 'running')   AS cancel_requested_count
                    FROM affected
                    """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                int cancelledCount = (int)reader.GetInt64(0);
                int cancelRequestedCount = (int)reader.GetInt64(1);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.LoadTasksCancelledInstrument(
                    _logger,
                    secid,
                    cancelledCount,
                    cancelRequestedCount,
                    elapsed);
                return new CancelResult(cancelledCount, cancelRequestedCount, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, LoadTasksTable, ex.GetType().Name);
                throw;
            }
        }
    }
}
