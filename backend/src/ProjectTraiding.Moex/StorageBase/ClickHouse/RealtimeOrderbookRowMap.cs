using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using System;
using System.Collections.Generic;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки стакана в moex_orderbook (V014). Одна карта на оба рынка: структура
    /// акций и фьючерсов совпадает, различает их board_id.
    ///
    /// Ключ сортировки secid + source_time + buy_sell + price. Проверено живым ответом: SEQNUM
    /// один на весь снимок, цены внутри стороны не повторяются.
    ///
    /// source_time собирается из SEQNUM — в ответе стакана даты нет, UPDATETIME даёт только
    /// время суток. Ни seq_num, ни update_time столбцами не хранятся: оба выводятся из
    /// source_time без потерь, а производные значения столбцами не хранятся.
    /// </summary>
    public sealed class RealtimeOrderbookRowMap : IRowMap<RealtimeOrderbookRowDTO>
    {
        public string Table => "moex_orderbook";

        public string TokenPrefix => "orderbook:snapshot";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "buy_sell", "price", "board_id", "quantity", "decimals",
            "ingest_priority"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["buy_sell"] = "LowCardinality(String)",
                ["price"] = "Float64",
                ["board_id"] = "LowCardinality(Nullable(String))",
                ["quantity"] = "Nullable(Int64)",
                ["decimals"] = "Nullable(Int64)",
                ["ingest_priority"] = "UInt8",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Запись стакана отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(RealtimeOrderbookRowDTO item, string secid)
        {
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTimeFromSeqNum(item.SeqNum);

            // buy_sell и price ключевые и обязательные: без них строка не опознаётся.
            if (string.IsNullOrWhiteSpace(item.BuySell))
                throw new InvalidOperationException("Строка стакана отвергнута: BUYSELL пуст.");

            if (item.Price is null)
                throw new InvalidOperationException("Строка стакана отвергнута: PRICE пуст.");

            object?[] row =
            {
                secid,
                sourceTime,
                item.BuySell,
                item.Price.Value,
                item.BoardId,
                item.Quantity,
                item.Decimals,
                (byte)0,
            };

            return (row, sourceTime);
        }
    }
}
