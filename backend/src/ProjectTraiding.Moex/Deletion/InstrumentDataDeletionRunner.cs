using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Deletion
{
    /// <summary>Исход исполнения захваченной заявки удаления.</summary>
    public enum DeletionStatus
    {
        Done,
        LoadRunning,
        RealtimeEnabled,
    }

    public readonly record struct DeletionOutcome(
        DeletionStatus Status,
        int LoadedRangesDeleted,
        int StreamCursorsDeleted,
        int LoadTasksDeleted,
        int ClickHouseTablesCleared,
        TimeSpan Elapsed);

    /// <summary>
    /// Исполняет уже захваченную заявку удаления. Проверки после захвата не снимают
    /// заявку: очередь сохраняет её активной, а фоновый исполнитель отложит повтор.
    /// Порядок хранилищ сохраняется: ClickHouse → PostgreSQL.
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

        public async Task<DeletionOutcome> RunClaimedAsync(
            Guid deletionId,
            string secid,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();

            if (await _guard.HasRunningLoadAsync(secid, ct))
                return Rejected(DeletionStatus.LoadRunning, startTs);

            if (await _guard.HasEnabledRealtimeAsync(secid, ct))
                return Rejected(DeletionStatus.RealtimeEnabled, startTs);

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
