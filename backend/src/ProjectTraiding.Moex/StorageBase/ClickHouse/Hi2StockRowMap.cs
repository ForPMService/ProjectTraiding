using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки концентрации по акциям в moex_hi2_daily_stock (V008).
    /// Зерно 1 день. Ключ сортировки secid + source_time + metric.
    /// </summary>
    public sealed class Hi2StockRowMap : IRowMap<Hi2AssetDTO>
    {
        public string Table => "moex_hi2_daily_stock";

        public string TokenPrefix => "hi2:1d:stock";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "metric", "value", "reference", "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["metric"] = "LowCardinality(String)",
                ["value"] = "Nullable(Float64)",
                ["reference"] = "Nullable(String)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка концентрации отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(
            Hi2AssetDTO item, string secid, string? tradeSessionDate)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            if (string.IsNullOrWhiteSpace(item.Metric))
                throw new InvalidOperationException("Строка концентрации отвергнута: metric пустой.");

            object?[] row =
            {
                secid,
                sourceTime,
                item.Metric,
                item.Value,
                item.Reference,
                MoexClickHouseTime.AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }
    }
}
