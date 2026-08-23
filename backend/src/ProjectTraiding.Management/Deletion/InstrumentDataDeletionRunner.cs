using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Diagnostics;

namespace ProjectTraiding.Management.Deletion
{
    /// <summary>Исход команды удаления. Ровно один из исходов, без промежуточных.</summary>
    public enum DeletionStatus
    {
        Done,
        InstrumentNotFound,
        LoadRunning,
        RealtimeEnabled,
        AlreadyDeleting,
    }

    public readonly record struct DeletionOutcome(
        DeletionStatus Status,
        int LoadedRangesDeleted,
        int StreamCursorsDeleted,
        int LoadTasksDeleted,
        int ClickHouseTablesCleared,
        TimeSpan Elapsed);

    /// <summary>
    /// Ведёт удаление рабочих данных инструмента от проверок до закрытия задания.
    ///
    /// ПОРЯДОК ШАГОВ. Задание удаления ставится РАНЬШЕ проверок на работающую
    /// загрузку и включённый приём, а не позже. Обратный порядок оставлял бы окно
    /// шириной в промежуток между двумя запросами: за это время фоновый
    /// исполнитель контура Moex успевает перевести задание в running, и дальше
    /// удаление сносит строку работающего задания. Тогда
    /// MoexLoadTaskWriter.MarkDoneAsync падает с InvalidOperationException, а сама
    /// загрузка успевает дописать данные уже после очистки ClickHouse — инструмент
    /// остаётся с мусором при задании в статусе 'finished'. Признак, поставленный
    /// первым, отсекает подбор и ручной запуск, начатые после его фиксации,
    /// и тем сокращает окно до нуля для них. Команду, начатую раньше фиксации,
    /// он не останавливает — граница описана в разделе 5.3 задания, и она же
    /// причина, по которой удаление запускают после остановки загрузки и приёма.
    ///
    /// Если проверка после постановки признака запретила удаление, задание
    /// снимается — но только если его поставил этот запуск. Чужую строку продолжение
    /// не трогает никогда.
    ///
    /// ПОРЯДОК ХРАНИЛИЩ: ClickHouse → PostgreSQL.
    /// ClickHouse первым, потому что при обрыве между ним и PostgreSQL карта
    /// покрытия останется полной при отсутствующих данных, а не наоборот:
    /// пустая карта над живыми данными пустила бы повторную загрузку поверх них.
    /// ПРОДОЛЖЕНИЕ ПОСЛЕ ОБРЫВА. При обрыве задание остаётся в статусе 'started':
    /// очистка не завершена, новые загрузки запрещены. Продолжить его командой
    /// с resume = true — единственный способ довести удаление до конца, и он же
    /// единственная причина, по которой resume существует. Продолжение работает
    /// с ТОЙ ЖЕ строкой: не снимает её и не заводит новую. Снятие с последующей
    /// постановкой оставило бы промежуток без строки 'started', и в этот промежуток
    /// обычная постановка задания загрузки прошла бы успешно, а затем была бы молча
    /// стёрта продолжающейся очисткой.
    ///
    /// ЧЕГО RESUME НЕ ДЕЛАЕТ. Отличить оборванное удаление от идущего система
    /// не может: признака живости процесса в таблице нет и по условию задачи
    /// не заводится. Поэтому resume — ручная аварийная команда, и ответственность
    /// за то, что прошлая попытка действительно завершилась, лежит на операторе.
    /// Разбор последствий нарушения — в разделе 5 задания.
    /// </summary>
    public sealed class InstrumentDataDeletionRunner
    {
        private readonly InstrumentDeletionGuardReader _guard;
        private readonly InstrumentDeletionWriter _deletionWriter;
        private readonly InstrumentClickHouseDataDeleter _clickHouseDeleter;
        private readonly InstrumentPostgresDataDeleter _postgresDeleter;

        public InstrumentDataDeletionRunner(
            InstrumentDeletionGuardReader guard,
            InstrumentDeletionWriter deletionWriter,
            InstrumentClickHouseDataDeleter clickHouseDeleter,
            InstrumentPostgresDataDeleter postgresDeleter)
        {
            _guard = guard;
            _deletionWriter = deletionWriter;
            _clickHouseDeleter = clickHouseDeleter;
            _postgresDeleter = postgresDeleter;
        }

        public async Task<DeletionOutcome> RunAsync(
            string secid,
            bool resume,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();

            // Проверка справочника идёт первой и ничего не меняет: она отделяет
            // понятный отказ «инструмент не найден» от ошибки внешнего ключа 23503,
            // которую иначе выдала бы постановка задания.
            if (!await _guard.InstrumentExistsAsync(secid, ct))
                return Rejected(DeletionStatus.InstrumentNotFound, startTs);

            Guid? started = await _deletionWriter.TryStartAsync(secid, ct);
            bool startedHere = started is not null;

            if (!startedHere)
            {
                if (!resume)
                    return Rejected(DeletionStatus.AlreadyDeleting, startTs);

                started = await _deletionWriter.FindActiveAsync(secid, ct);
                if (started is null)
                {
                    // Строка исчезла между двумя запросами: состояние изменилось,
                    // оператор повторяет команду с чистого листа.
                    return Rejected(DeletionStatus.AlreadyDeleting, startTs);
                }
            }

            Guid deletionId = started!.Value;

            if (await _guard.HasRunningLoadAsync(secid, ct))
            {
                if (startedHere)
                    await _deletionWriter.RemoveAsync(deletionId, ct);

                return Rejected(DeletionStatus.LoadRunning, startTs);
            }

            if (await _guard.HasEnabledRealtimeAsync(secid, ct))
            {
                if (startedHere)
                    await _deletionWriter.RemoveAsync(deletionId, ct);

                return Rejected(DeletionStatus.RealtimeEnabled, startTs);
            }

            int tablesCleared = await _clickHouseDeleter.DeleteAsync(secid, ct);
            PostgresDeleteCounts postgres = await _postgresDeleter.DeleteAsync(secid, ct);

            await _deletionWriter.MarkFinishedAsync(deletionId, ct);

            return new DeletionOutcome(
                Status: DeletionStatus.Done,
                LoadedRangesDeleted: postgres.LoadedRangesDeleted,
                StreamCursorsDeleted: postgres.StreamCursorsDeleted,
                LoadTasksDeleted: postgres.LoadTasksDeleted,
                ClickHouseTablesCleared: tablesCleared,
                Elapsed: Stopwatch.GetElapsedTime(startTs));
        }

        private static DeletionOutcome Rejected(DeletionStatus status, long startTs) =>
            new DeletionOutcome(
                Status: status,
                LoadedRangesDeleted: 0,
                StreamCursorsDeleted: 0,
                LoadTasksDeleted: 0,
                ClickHouseTablesCleared: 0,
                Elapsed: Stopwatch.GetElapsedTime(startTs));
    }
}
