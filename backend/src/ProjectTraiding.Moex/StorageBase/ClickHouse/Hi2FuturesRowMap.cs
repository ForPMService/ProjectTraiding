using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки концентрации по фьючерсам в moex_hi2_daily_futures (V009).
    /// Зерно 1 день. Ключ сортировки secid + source_time + metric.
    /// </summary>
    public sealed class Hi2FuturesRowMap : IRowMap<Hi2FuturesDTO>
    {
        public string Table => "moex_hi2_daily_futures";

        public string TokenPrefix => "hi2:1d:futures";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "asset_code", "metric", "value", "reference", "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["asset_code"] = "LowCardinality(Nullable(String))",
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

        public (object?[] Row, DateTime Time) ToRow(Hi2FuturesDTO item, string secid)
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
                item.AssetCode,
                item.Metric,
                item.Value,
                item.Reference,
                MoexClickHouseTime.AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }
    }
}
