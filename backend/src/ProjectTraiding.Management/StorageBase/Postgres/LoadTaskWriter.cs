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
    }
}
