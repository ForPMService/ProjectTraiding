using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки открытого интереса по фьючерсам в moex_futoi_5m (V007).
    /// Зерно 5 минут. Ключ сортировки secid + source_time + clgroup.
    /// </summary>
    public sealed class FutoiRowMap : IRowMap<FutoiDTO>
    {
        public string Table => "moex_futoi_5m";

        public string TokenPrefix => "futoi:5m:futures";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "clgroup",
            "sess_id", "seqnum",
            "pos", "pos_long", "pos_short", "pos_long_num", "pos_short_num",
            "trade_session_date", "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["clgroup"] = "LowCardinality(String)",
                ["sess_id"] = "Nullable(Int32)",
                ["seqnum"] = "Nullable(Int32)",
                ["pos"] = "Nullable(Int64)",
                ["pos_long"] = "Nullable(Int64)",
                ["pos_short"] = "Nullable(Int64)",
                ["pos_long_num"] = "Nullable(Int64)",
                ["pos_short_num"] = "Nullable(Int64)",
                ["trade_session_date"] = "Nullable(String)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка открытого интереса отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(
            FutoiDTO item, string secid, string? tradeSessionDate)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            if (string.IsNullOrWhiteSpace(item.ClGroup))
                throw new InvalidOperationException("Строка открытого интереса отвергнута: clgroup пустой.");

            object?[] row =
            {
                secid,
                sourceTime,
                item.ClGroup,
                item.SessId,
                item.SeqNum,
                item.Pos,
                item.PosLong,
                item.PosShort,
                item.PosLongNum,
                item.PosShortNum,
                item.TradeSessionDate,
                MoexClickHouseTime.AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }
    }
}
