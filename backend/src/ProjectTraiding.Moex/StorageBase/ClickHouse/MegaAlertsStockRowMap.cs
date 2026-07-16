using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки мегаалёртов по акциям в moex_megaalerts_stock (V010).
    /// Событийное зерно. Ключ сортировки secid + source_time + alert_type.
    /// </summary>
    public sealed class MegaAlertsStockRowMap : IRowMap<MegaAlertsAssetsDTO>
    {
        public string Table => "moex_megaalerts_stock";

        public string TokenPrefix => "megaalerts:event:stock";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time", "alert_type", "threshold", "value", "reference", "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["alert_type"] = "LowCardinality(String)",
                ["threshold"] = "Nullable(Float64)",
                ["value"] = "Nullable(Float64)",
                ["reference"] = "Nullable(String)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка мегаалёртов отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(
            MegaAlertsAssetsDTO item, string secid, string? tradeSessionDate)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            if (string.IsNullOrWhiteSpace(item.AlertType))
                throw new InvalidOperationException("Строка мегаалёртов отвергнута: alert_type пустой.");

            object?[] row =
            {
                secid,
                sourceTime,
                item.AlertType,
                item.Threshold,
                item.Value,
                item.Reference,
                MoexClickHouseTime.AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }
    }
}
