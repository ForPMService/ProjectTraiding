using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using System;
using System.Collections.Generic;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки ленты сделок по фьючерсам в moex_trades_futures (V014).
    /// Зерно — одна сделка. Ключ сортировки secid + source_time + trade_no.
    ///
    /// Набор колонок иной, чем у акций: BOARDNAME вместо BOARDID; есть RECNO, OPENPOSITION,
    /// OFFMARKETDEAL; нет VALUE, PERIOD, DECIMALS, TRADINGSESSION.
    ///
    /// source_time и trade_session_date у фьючерсов расходятся на торгах выходного дня: сделка
    /// субботы относится к сессии понедельника. Обе величины хранятся, подменять одну другой — ошибка.
    /// </summary>
    public sealed class RealtimeTradesFuturesRowMap : IRowMap<RealtimeTradesFuturesDTO>
    {
        public string Table => "moex_trades_futures";

        public string TokenPrefix => "trades:tick:futures";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "trade_no", "board_name", "price", "quantity",
            "rec_no", "open_position", "off_market_deal", "buy_sell",
            "trade_session_date", "systime", "ingest_priority"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["trade_no"] = "Int64",
                ["board_name"] = "LowCardinality(Nullable(String))",
                ["price"] = "Nullable(Float64)",
                ["quantity"] = "Nullable(Int64)",
                ["rec_no"] = "Nullable(Int64)",
                ["open_position"] = "Nullable(Int64)",
                ["off_market_deal"] = "Nullable(Int32)",
                ["buy_sell"] = "LowCardinality(Nullable(String))",
                ["trade_session_date"] = "Nullable(Date)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
                ["ingest_priority"] = "UInt8",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Запись сделок отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(RealtimeTradesFuturesDTO item, string secid)
        {
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            if (item.TradeNo is null)
                throw new InvalidOperationException("Сделка отвергнута: TRADENO пуст.");

            object?[] row =
            {
                secid,
                sourceTime,
                item.TradeNo.Value,
                item.BoardName,
                item.Price,
                item.Quantity,
                item.RecNo,
                item.OpenPosition,
                item.OffMarketDeal,
                item.BuySell,
                MoexClickHouseTime.ParseDate(item.TradeSessionDate),
                MoexClickHouseTime.ParseWallClock(item.SysTime),
                (byte)0,
            };

            return (row, sourceTime);
        }
    }
}
