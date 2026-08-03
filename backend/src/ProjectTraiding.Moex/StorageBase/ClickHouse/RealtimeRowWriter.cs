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
    /// Первый уровень дедупликации — insert_deduplication_token в пределах настроенного окна
    /// non_replicated_deduplication_window: ClickHouse отсекает повторный INSERT с тем же токеном.
    /// Токен воспроизводится по инструменту, временны́м границам пачки и числу строк, но это
    /// ограниченная защита близкого повторного INSERT, а не доказательство идентичности содержимого
    /// и не строковая уникальность.
    ///
    /// Второй уровень — ReplacingMergeTree(ingest_priority), заданный миграцией V015: при слиянии
    /// он схлопывает строки с одинаковым ключом сортировки. Именно этот уровень снимает версии из
    /// расширенной пачки, которая приходит с другим токеном и проходит первый уровень. До фонового
    /// слияния чтение без дублей требует FINAL; это обязанность читателей, а не приёмника.
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
            StorageInsertContext insertContext,
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
            await _executor.InsertAsync(
                _map.Table, _map.Columns, _map.ColumnTypes, rows, token, insertContext, ct);

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
