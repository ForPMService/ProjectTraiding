using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using System;
using System.Collections.Generic;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки ленты сделок по акциям в moex_trades_stock (V014).
    /// Зерно — одна сделка. Ключ сортировки secid + source_time + trade_no.
    ///
    /// trade_no ключевой и обязательный: время источника имеет точность секунды, а в одну
    /// секунду попадает до 427 сделок — время их не различает.
    /// </summary>
    public sealed class RealtimeTradesStockRowMap : IRowMap<RealtimeTradesStockDTO>
    {
        public string Table => "moex_trades_stock";

        public string TokenPrefix => "trades:tick:stock";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "trade_no", "board_id", "price", "quantity", "value",
            "period", "buy_sell", "decimals", "trading_session", "trade_session_date", "systime",
            "ingest_priority"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["trade_no"] = "Int64",
                ["board_id"] = "LowCardinality(Nullable(String))",
                ["price"] = "Nullable(Float64)",
                ["quantity"] = "Nullable(Int64)",
                ["value"] = "Nullable(Float64)",
                ["period"] = "LowCardinality(Nullable(String))",
                ["buy_sell"] = "LowCardinality(Nullable(String))",
                ["decimals"] = "Nullable(Int32)",
                ["trading_session"] = "LowCardinality(Nullable(String))",
                ["trade_session_date"] = "Nullable(Date)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
                ["ingest_priority"] = "UInt8",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Запись сделок отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(
            RealtimeTradesStockDTO item, string secid, string? tradeSessionDate)
        {
            // source_time — ключевой not-null столбец: фактический момент сделки.
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            // trade_no — второй ключевой not-null столбец. Пустой номер означает, что источник
            // отдал сделку без опознавательного знака: такую строку хранить нельзя.
            if (item.TradeNo is null)
                throw new InvalidOperationException("Сделка отвергнута: TRADENO пуст.");

            object?[] row =
            {
                secid,
                sourceTime,
                item.TradeNo.Value,
                item.BoardId,
                item.Price,
                item.Quantity,
                item.Value,
                item.Period,
                item.BuySell,
                item.Decimals,
                item.TradingSession,
                MoexClickHouseTime.ParseDate(tradeSessionDate),
                MoexClickHouseTime.ParseWallClock(item.SysTime),
                (byte)0,
            };

            return (row, sourceTime);
        }
    }
}
