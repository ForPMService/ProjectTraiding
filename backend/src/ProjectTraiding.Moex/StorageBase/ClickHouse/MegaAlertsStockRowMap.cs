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

        public (object?[] Row, DateTime Time) ToRow(MegaAlertsAssetsDTO item, string secid)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = BuildSourceTime(item.TradeDate, item.TradeTime);

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
