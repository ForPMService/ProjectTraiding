using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки статистики стакана по фьючерсам в moex_ob_stats_5m_futures (V005).
    /// Зерно 5 минут. Ключ сортировки secid + source_time.
    /// </summary>
    public sealed class ObStatsFuturesRowMap : IRowMap<SuperCandlesFuturesOrderBookStats5mDTO>
    {
        public string Table => "moex_ob_stats_5m_futures";

        public string TokenPrefix => "obstats:5m:futures";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "asset_code",
            "mid_price", "micro_price",
            "spread_l1", "spread_l2", "spread_l3", "spread_l5", "spread_l10", "spread_l20",
            "levels_b", "levels_s",
            "vol_b_l1", "vol_b_l2", "vol_b_l3", "vol_b_l5", "vol_b_l10", "vol_b_l20",
            "vol_s_l1", "vol_s_l2", "vol_s_l3", "vol_s_l5", "vol_s_l10", "vol_s_l20",
            "vwap_b_l3", "vwap_b_l5", "vwap_b_l10", "vwap_b_l20",
            "vwap_s_l3", "vwap_s_l5", "vwap_s_l10", "vwap_s_l20",
            "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["asset_code"] = "LowCardinality(Nullable(String))",
                ["mid_price"] = "Nullable(Float64)",
                ["micro_price"] = "Nullable(Float64)",
                ["spread_l1"] = "Nullable(Float64)",
                ["spread_l2"] = "Nullable(Float64)",
                ["spread_l3"] = "Nullable(Float64)",
                ["spread_l5"] = "Nullable(Float64)",
                ["spread_l10"] = "Nullable(Float64)",
                ["spread_l20"] = "Nullable(Float64)",
                ["levels_b"] = "Nullable(Int32)",
                ["levels_s"] = "Nullable(Int32)",
                ["vol_b_l1"] = "Nullable(Int64)",
                ["vol_b_l2"] = "Nullable(Int64)",
                ["vol_b_l3"] = "Nullable(Int64)",
                ["vol_b_l5"] = "Nullable(Int64)",
                ["vol_b_l10"] = "Nullable(Int64)",
                ["vol_b_l20"] = "Nullable(Int64)",
                ["vol_s_l1"] = "Nullable(Int64)",
                ["vol_s_l2"] = "Nullable(Int64)",
                ["vol_s_l3"] = "Nullable(Int64)",
                ["vol_s_l5"] = "Nullable(Int64)",
                ["vol_s_l10"] = "Nullable(Int64)",
                ["vol_s_l20"] = "Nullable(Int64)",
                ["vwap_b_l3"] = "Nullable(Float64)",
                ["vwap_b_l5"] = "Nullable(Float64)",
                ["vwap_b_l10"] = "Nullable(Float64)",
                ["vwap_b_l20"] = "Nullable(Float64)",
                ["vwap_s_l3"] = "Nullable(Float64)",
                ["vwap_s_l5"] = "Nullable(Float64)",
                ["vwap_s_l10"] = "Nullable(Float64)",
                ["vwap_s_l20"] = "Nullable(Float64)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка статистики стакана отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(SuperCandlesFuturesOrderBookStats5mDTO item, string secid)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            object?[] row =
            {
                secid,
                sourceTime,
                item.AssetCode,
                item.MidPrice,
                item.MicroPrice,
                item.SpreadL1,
                item.SpreadL2,
                item.SpreadL3,
                item.SpreadL5,
                item.SpreadL10,
                item.SpreadL20,
                item.LevelsB,
                item.LevelsS,
                item.VolBL1,
                item.VolBL2,
                item.VolBL3,
                item.VolBL5,
                item.VolBL10,
                item.VolBL20,
                item.VolSL1,
                item.VolSL2,
                item.VolSL3,
                item.VolSL5,
                item.VolSL10,
                item.VolSL20,
                item.VwapBL3,
                item.VwapBL5,
                item.VwapBL10,
                item.VwapBL20,
                item.VwapSL3,
                item.VwapSL5,
                item.VwapSL10,
                item.VwapSL20,
                MoexClickHouseTime.AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }
    }
}
