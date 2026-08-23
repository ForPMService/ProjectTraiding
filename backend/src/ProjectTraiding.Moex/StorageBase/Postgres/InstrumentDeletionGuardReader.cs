using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Проверки, отвечающие на вопрос «можно ли сейчас удалять данные инструмента».
    /// Читает рабочие таблицы и ничего не пишет. Вызывается координатором удаления
    /// после захвата заявки, поэтому отказ остаётся активной заявкой очереди.
    /// </summary>
    public sealed class InstrumentDeletionGuardReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public InstrumentDeletionGuardReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        /// <summary>
        /// По инструменту прямо сейчас пишутся данные загрузчиком.
        ///
        /// Интересует ровно один статус — running. Задания pending и partial ничего
        /// не пишут и удалению не мешают; done, error и cancelled — завершённые исходы.
        /// </summary>
        public async Task<bool> HasRunningLoadAsync(string secid, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1 FROM moex_load_tasks
                    WHERE secid = @secid AND status = 'running'
                )
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            object? result = await cmd.ExecuteScalarAsync(ct);
            return result is bool exists && exists;
        }

        /// <summary>По инструменту включён приём реального времени хотя бы одного вида.</summary>
        public async Task<bool> HasEnabledRealtimeAsync(string secid, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1 FROM moex_realtime_subscriptions
                    WHERE secid = @secid AND enabled
                )
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            object? result = await cmd.ExecuteScalarAsync(ct);
            return result is bool exists && exists;
        }
    }
}
