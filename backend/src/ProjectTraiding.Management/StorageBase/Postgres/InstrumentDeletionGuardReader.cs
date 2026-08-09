using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    /// <summary>
    /// Проверки, отвечающие на вопрос «можно ли сейчас удалять данные инструмента».
    /// Читает три таблицы и ничего не пишет. Вызывается только координатором удаления.
    ///
    /// Метода «идёт ли по инструменту удаление» здесь НЕТ, и это не упущение.
    /// Запрет на новые запуски записи живёт внутри самих пишущих команд
    /// (коммиты 3 и 4). Отдельная проверка перед записью оставляет окно шириной
    /// в промежуток между двумя запросами; условие внутри запроса сокращает окно
    /// до нуля для всех команд, начатых после фиксации строки удаления. Команду,
    /// начатую раньше, ни то ни другое не остановит — это граница механизма,
    /// а не его дефект, и она описана в разделе 5.3 задания.
    /// </summary>
    public sealed class InstrumentDeletionGuardReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public InstrumentDeletionGuardReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        /// <summary>Инструмент известен справочнику.</summary>
        public async Task<bool> InstrumentExistsAsync(string secid, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM moex_instruments WHERE secid = @secid)",
                connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            object? result = await cmd.ExecuteScalarAsync(ct);
            return result is bool exists && exists;
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
