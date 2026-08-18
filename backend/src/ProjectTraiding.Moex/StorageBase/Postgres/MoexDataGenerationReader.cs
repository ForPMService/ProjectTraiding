using Npgsql;
using NpgsqlTypes;
using System;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Чтение поколения данных инструмента: идентификатор последнего завершённого удаления
    /// из moex_instrument_data_deletions. Поколение входит в токен дедупликации ClickHouse,
    /// потому что контрольные суммы вставленных блоков переживают мутацию ALTER ... DELETE:
    /// без смены поколения повторная загрузка удалённого диапазона молча отсекается.
    ///
    /// Значение читается один раз — на исполнение задания в истории и на открытие состояния
    /// инструмента в приёме — и остаётся неизменным до конца этой работы. В истории это
    /// подкреплено кодом: захват задания запрещён при незакрытом удалении, а удаление
    /// отказывается идти при работающей загрузке. В приёме действует существующая
    /// эксплуатационная граница: удаление запускается после остановки приёма по инструменту,
    /// потому что уже начавшийся оборот опроса доработает по прежнему списку наблюдения.
    /// </summary>
    public sealed class MoexDataGenerationReader
    {
        /// <summary>Поколение до первого состоявшегося удаления.</summary>
        public const string InitialGeneration = "initial";

        private readonly NpgsqlDataSource _dataSource;

        public MoexDataGenerationReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        /// <summary>
        /// Поколение данных по коду инструмента. Для открытого интереса код — это код серии:
        /// подстановка сделана при создании задания, повторно разрешать связи не нужно.
        /// </summary>
        public async Task<string> GetAsync(string secid, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT id
                FROM moex_instrument_data_deletions
                WHERE secid = @secid AND status = 'finished'
                ORDER BY created_at DESC
                LIMIT 1
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            object? value = await cmd.ExecuteScalarAsync(ct);
            if (value is Guid id)
                return id.ToString("N");

            return InitialGeneration;
        }
    }
}
