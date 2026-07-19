using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Прямой писатель одной готовой пачки строк приёма в ClickHouse. В отличие от RowWriter,
    /// не знает про задачи загрузки и прогресс: приёмник вызывает опрос, получает готовый список
    /// и пишет его одним INSERT. Форму вставки задаёт карта столбцов; исполнитель и механизм
    /// токена — общие с исторической загрузкой.
    ///
    /// При точном повторе пачки с теми же временны́ми границами и числом строк формируется тот
    /// же токен, и ClickHouse отсекает повтор в пределах настроенного окна дедупликации MergeTree.
    /// Это ограниченная защита близкого повторного INSERT, а не доказательство идентичности
    /// содержимого и не строковая уникальность.
    /// </summary>
    /// <typeparam name="T">Тип строки приёма (сделка или строка стакана).</typeparam>
    public sealed class RealtimeRowWriter<T>
    {
        private readonly ClickHouseInsertExecutor _executor;
        private readonly IRowMap<T> _map;
        private readonly ILogger<RealtimeRowWriter<T>> _logger;

        public RealtimeRowWriter(
            ClickHouseInsertExecutor executor,
            IRowMap<T> map,
            ILogger<RealtimeRowWriter<T>> logger)
        {
            _executor = executor;
            _map = map;
            _logger = logger;
        }

        /// <summary>
        /// Пишет готовый список строк одним INSERT. Пустой список — ноль вставок, не ошибка.
        /// Возвращает число строк, отданных на вставку (для сердцебиения покрытия).
        ///
        /// tradeSessionDate — дата торговой сессии из блока dataversion ответа, общая для всех
        /// строк пачки. Проставляется каждой строке через карту. null — столбец останется NULL.
        /// </summary>
        public async Task<long> WriteAsync(
            string secid,
            IReadOnlyList<T> items,
            string? tradeSessionDate,
            CancellationToken ct)
        {
            if (items.Count == 0)
                return 0;

            _map.EnsureRangeValid(secid);

            List<object?[]> rows = new List<object?[]>(items.Count);
            DateTime firstTime = default;
            DateTime lastTime = default;

            for (int i = 0; i < items.Count; i++)
            {
                (object?[] row, DateTime time) = _map.ToRow(
                    items[i], secid, tradeSessionDate);
                if (i == 0)
                    firstTime = time;
                lastTime = time;
                rows.Add(row);
            }

            string token = BuildToken(secid, firstTime, lastTime, rows.Count);
            await _executor.InsertAsync(_map.Table, _map.Columns, _map.ColumnTypes, rows, token, ct);

            return items.Count;
        }

        // {префикс}:{secid}:{время первой}:{время последней}:{число строк}.
        // Формат времени инвариантный — границы пачки воспроизводимы при повторе.
        private string BuildToken(string secid, DateTime first, DateTime last, int count)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{_map.TokenPrefix}:{secid}:{first:yyyyMMddHHmmssfff}:{last:yyyyMMddHHmmssfff}:{count}");
        }
    }
}
