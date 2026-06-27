using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки статистики сделок по фьючерсам в moex_trade_stats_5m_futures (V003).
    /// Зерно 5 минут. Ключ сортировки secid + source_time.
    /// </summary>
    public sealed class TradeStatsFuturesRowMap : IRowMap<SuperCandlesFuturesTradeStats5mDTO>
    {
        public string Table => "moex_trade_stats_5m_futures";

        public string TokenPrefix => "tradestats:5m:futures";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "asset_code",
            "pr_open", "pr_high", "pr_low", "pr_close", "pr_std",
            "vol", "val", "trades", "pr_vwap", "pr_change",
            "trades_b", "trades_s", "val_b", "val_s", "vol_b", "vol_s",
            "disb", "pr_vwap_b", "pr_vwap_s",
            "im", "oi_open", "oi_high", "oi_low", "oi_close",
            "sec_pr_open", "sec_pr_high", "sec_pr_low", "sec_pr_close",
            "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["asset_code"] = "LowCardinality(Nullable(String))",
                ["pr_open"] = "Nullable(Float64)",
                ["pr_high"] = "Nullable(Float64)",
                ["pr_low"] = "Nullable(Float64)",
                ["pr_close"] = "Nullable(Float64)",
                ["pr_std"] = "Nullable(Float64)",
                ["vol"] = "Nullable(Int64)",
                ["val"] = "Nullable(Int64)",
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
                ["im"] = "Nullable(Float64)",
                ["oi_open"] = "Nullable(Int64)",
                ["oi_high"] = "Nullable(Int64)",
                ["oi_low"] = "Nullable(Int64)",
                ["oi_close"] = "Nullable(Int64)",
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

        public (object?[] Row, DateTime Time) ToRow(SuperCandlesFuturesTradeStats5mDTO item, string secid)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = BuildSourceTime(item.TradeDate, item.TradeTime);

            object?[] row =
            {
                secid,
                sourceTime,
                item.AssetCode,
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
                item.Im,
                item.OiOpen,
                item.OiHigh,
                item.OiLow,
                item.OiClose,
                item.SecPrOpen,
                item.SecPrHigh,
                item.SecPrLow,
                item.SecPrClose,
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
