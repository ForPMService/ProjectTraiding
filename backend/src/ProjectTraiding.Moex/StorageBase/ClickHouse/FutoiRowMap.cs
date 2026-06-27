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

        public (object?[] Row, DateTime Time) ToRow(FutoiDTO item, string secid)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = BuildSourceTime(item.TradeDate, item.TradeTime);

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
                AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }

        // Дата "yyyy-MM-dd" и время "HH:mm:ss" из источника в одно московское стенное время.
        // Kind=Unspecified — bulk insert трактует его как стенное время зоны столбца без сдвига.
        private static DateTime BuildSourceTime(string? tradeDate, string? tradeTime)
        {
            if (string.IsNullOrWhiteSpace(tradeDate) || string.IsNullOrWhiteSpace(tradeTime))
                throw new InvalidOperationException(
                    "Строка статистики отвергнута: пустые дата или время торгов.");

            DateTime parsed = DateTime.ParseExact(
                $"{tradeDate} {tradeTime}",
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        private static DateTime? AsWallClock(DateTime? value)
        {
            if (value is null)
                return null;

            return DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        }
    }
}
