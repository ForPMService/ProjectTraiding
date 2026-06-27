using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки статистики стакана по акциям в moex_ob_stats_5m_stock (V004).
    /// Зерно 5 минут. Ключ сортировки secid + source_time.
    /// </summary>
    public sealed class ObStatsStockRowMap : IRowMap<SuperCandlesOrderBookStats5mDTO>
    {
        public string Table => "moex_ob_stats_5m_stock";

        public string TokenPrefix => "obstats:5m:stock";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time",
            "spread_bbo", "spread_lv10", "spread_1mio",
            "levels_b", "levels_s",
            "vol_b", "vol_s", "val_b", "val_s",
            "imbalance_vol_bbo", "imbalance_val_bbo",
            "imbalance_vol", "imbalance_val",
            "vwap_b", "vwap_s", "vwap_b_1mio", "vwap_s_1mio",
            "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["spread_bbo"] = "Nullable(Float64)",
                ["spread_lv10"] = "Nullable(Float64)",
                ["spread_1mio"] = "Nullable(Float64)",
                ["levels_b"] = "Nullable(Int32)",
                ["levels_s"] = "Nullable(Int32)",
                ["vol_b"] = "Nullable(Int64)",
                ["vol_s"] = "Nullable(Int64)",
                ["val_b"] = "Nullable(Int64)",
                ["val_s"] = "Nullable(Int64)",
                ["imbalance_vol_bbo"] = "Nullable(Float64)",
                ["imbalance_val_bbo"] = "Nullable(Float64)",
                ["imbalance_vol"] = "Nullable(Float64)",
                ["imbalance_val"] = "Nullable(Float64)",
                ["vwap_b"] = "Nullable(Float64)",
                ["vwap_s"] = "Nullable(Float64)",
                ["vwap_b_1mio"] = "Nullable(Float64)",
                ["vwap_s_1mio"] = "Nullable(Float64)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка статистики стакана отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(SuperCandlesOrderBookStats5mDTO item, string secid)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = BuildSourceTime(item.TradeDate, item.TradeTime);

            object?[] row =
            {
                secid,
                sourceTime,
                item.SpreadBbo,
                item.SpreadLv10,
                item.Spread1Mio,
                item.LevelsB,
                item.LevelsS,
                item.VolB,
                item.VolS,
                item.ValB,
                item.ValS,
                item.ImbalanceVolBbo,
                item.ImbalanceValBbo,
                item.ImbalanceVol,
                item.ImbalanceVal,
                item.VwapB,
                item.VwapS,
                item.VwapB1Mio,
                item.VwapS1Mio,
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
