using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки статистики сделок по акциям в moex_trade_stats_5m_stock (V002).
    /// Зерно 5 минут. Ключ сортировки secid + source_time. В отличие от свечей метка времени
    /// не приходит готовой — собирается из даты и времени торгов в московское стенное время.
    /// </summary>
    public sealed class TradeStatsStockRowMap : IRowMap<SuperCandlesTradeStats5mDTO>
    {
        public string Table => "moex_trade_stats_5m_stock";

        public string TokenPrefix => "tradestats:5m:stock";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time",
            "pr_open", "pr_high", "pr_low", "pr_close", "pr_std",
            "vol", "val", "trades", "pr_vwap", "pr_change",
            "trades_b", "trades_s", "val_b", "val_s", "vol_b", "vol_s",
            "disb", "pr_vwap_b", "pr_vwap_s",
            "sec_pr_open", "sec_pr_high", "sec_pr_low", "sec_pr_close",
            "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["pr_open"] = "Nullable(Float64)",
                ["pr_high"] = "Nullable(Float64)",
                ["pr_low"] = "Nullable(Float64)",
                ["pr_close"] = "Nullable(Float64)",
                ["pr_std"] = "Nullable(Float64)",
                ["vol"] = "Nullable(Int32)",
                ["val"] = "Nullable(Float64)",
                ["trades"] = "Nullable(Int32)",
                ["pr_vwap"] = "Nullable(Float64)",
                ["pr_change"] = "Nullable(Float64)",
                ["trades_b"] = "Nullable(Int32)",
                ["trades_s"] = "Nullable(Int32)",
                ["val_b"] = "Nullable(Float64)",
                ["val_s"] = "Nullable(Float64)",
                ["vol_b"] = "Nullable(Int64)",
                ["vol_s"] = "Nullable(Int64)",
                ["disb"] = "Nullable(Float64)",
                ["pr_vwap_b"] = "Nullable(Float64)",
                ["pr_vwap_s"] = "Nullable(Float64)",
                ["sec_pr_open"] = "Nullable(Int32)",
                ["sec_pr_high"] = "Nullable(Int32)",
                ["sec_pr_low"] = "Nullable(Int32)",
                ["sec_pr_close"] = "Nullable(Int32)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка статистики сделок отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(
            SuperCandlesTradeStats5mDTO item, string secid, string? tradeSessionDate)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            object?[] row =
            {
                secid,
                sourceTime,
                item.PrOpen,
                item.PrHigh,
                item.PrLow,
                item.PrClose,
                item.PrStd,
                item.Vol,
                item.Val,
                item.Trades,
                item.PrVwap,
                item.PrChange,
                item.TradesB,
                item.TradesS,
                item.ValB,
                item.ValS,
                item.VolB,
                item.VolS,
                item.Disb,
                item.PrVwapB,
                item.PrVwapS,
                item.SecPrOpen,
                item.SecPrHigh,
                item.SecPrLow,
                item.SecPrClose,
                MoexClickHouseTime.AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }
    }
}
