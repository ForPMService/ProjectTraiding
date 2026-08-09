using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    /// <summary>
    /// Владелец таблицы moex_instrument_data_deletions: постановка задания удаления,
    /// его поиск для продолжения, закрытие и снятие несостоявшегося.
    /// </summary>
    public sealed class InstrumentDeletionWriter
    {
        private const string Table = "moex_instrument_data_deletions";
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<InstrumentDeletionWriter> _logger;

        public InstrumentDeletionWriter(
            NpgsqlDataSource dataSource,
            ILogger<InstrumentDeletionWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        /// <summary>
        /// Ставит задание удаления со статусом 'started'.
        /// Возврат null означает, что по инструменту уже есть незакрытая строка.
        ///
        /// Конфликт разбирается базой по частичному уникальному индексу, а не
        /// проверкой перед вставкой: между отдельными SELECT и INSERT второй
        /// оператор успел бы вклиниться, между ON CONFLICT и вставкой — не успеет.
        /// </summary>
        public async Task<Guid?> TryStartAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_instrument_data_deletions (secid)
                VALUES (@secid)
                ON CONFLICT (secid) WHERE status = 'started'
                DO NOTHING
                RETURNING id
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            object? idObj = await cmd.ExecuteScalarAsync(ct);
            return idObj is Guid id ? id : (Guid?)null;
        }

        /// <summary>
        /// Возвращает идентификатор незакрытого задания инструмента, если оно есть.
        ///
        /// Нужен продолжению прерванного удаления. Прерванное удаление оставляет
        /// строку 'started' — это верно, потому что очистка не завершена и загрузки
        /// запускать нельзя. Продолжение работает с ТОЙ ЖЕ строкой, не снимая её
        /// и не заводя новую: снятие с последующей постановкой оставило бы промежуток,
        /// в котором строки 'started' нет вовсе, и в этот промежуток обычная
        /// постановка задания загрузки прошла бы успешно — а затем была бы молча
        /// стёрта продолжающейся очисткой. Кроме того, та же строка сохраняет
        /// created_at исходного запуска, то есть историю, ради которой таблица заведена.
        /// </summary>
        public async Task<Guid?> FindActiveAsync(string secid, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT id FROM moex_instrument_data_deletions
                WHERE secid = @secid AND status = 'started'
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            object? idObj = await cmd.ExecuteScalarAsync(ct);
            return idObj is Guid id ? id : (Guid?)null;
        }

        /// <summary>
        /// Закрывает задание: 'started' → 'finished'. Вызывается только после того,
        /// как данные удалены из всех трёх хранилищ.
        /// </summary>
        public async Task MarkFinishedAsync(Guid deletionId, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_instrument_data_deletions
                SET status = 'finished'
                WHERE id = @id AND status = 'started'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = deletionId;

            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Снимает своё задание, которое не состоялось: строка удаляется целиком.
        ///
        /// Единственный случай снятия. Вызывается, когда задание было поставлено
        /// этим же запуском, но проверки после постановки запретили удаление,
        /// и ни одно хранилище тронуто не было. Перевод такой строки в 'finished'
        /// был бы ложью — история утверждала бы, что удаление выполнено.
        /// Удаление строки говорит правду: удаления не было.
        /// </summary>
        public async Task RemoveAsync(Guid deletionId, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                DELETE FROM moex_instrument_data_deletions
                WHERE id = @id AND status = 'started'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = deletionId;

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
