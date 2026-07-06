using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки статистики заявок по акциям в moex_order_stats_5m_stock (V006).
    /// Зерно 5 минут. Ключ сортировки secid + source_time.
    /// </summary>
    public sealed class OrderStatsStockRowMap : IRowMap<SuperCandlesOrderStats5mDTO>
    {
        public string Table => "moex_order_stats_5m_stock";

        public string TokenPrefix => "orderstats:5m:stock";

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "source_time",
            "put_orders_b", "put_orders_s", "put_val_b", "put_val_s",
            "put_vol_b", "put_vol_s", "put_vwap_b", "put_vwap_s",
            "put_vol", "put_val", "put_orders",
            "cancel_orders_b", "cancel_orders_s", "cancel_val_b", "cancel_val_s",
            "cancel_vol_b", "cancel_vol_s", "cancel_vwap_b", "cancel_vwap_s",
            "cancel_vol", "cancel_val", "cancel_orders",
            "systime"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["source_time"] = "DateTime64(3, 'Europe/Moscow')",
                ["put_orders_b"] = "Nullable(Int32)",
                ["put_orders_s"] = "Nullable(Int32)",
                ["put_val_b"] = "Nullable(Float64)",
                ["put_val_s"] = "Nullable(Float64)",
                ["put_vol_b"] = "Nullable(Int32)",
                ["put_vol_s"] = "Nullable(Int32)",
                ["put_vwap_b"] = "Nullable(Float64)",
                ["put_vwap_s"] = "Nullable(Float64)",
                ["put_vol"] = "Nullable(Int32)",
                ["put_val"] = "Nullable(Float64)",
                ["put_orders"] = "Nullable(Int32)",
                ["cancel_orders_b"] = "Nullable(Int32)",
                ["cancel_orders_s"] = "Nullable(Int32)",
                ["cancel_val_b"] = "Nullable(Float64)",
                ["cancel_val_s"] = "Nullable(Float64)",
                ["cancel_vol_b"] = "Nullable(Int32)",
                ["cancel_vol_s"] = "Nullable(Int64)",
                ["cancel_vwap_b"] = "Nullable(Float64)",
                ["cancel_vwap_s"] = "Nullable(Float64)",
                ["cancel_vol"] = "Nullable(Int64)",
                ["cancel_val"] = "Nullable(Float64)",
                ["cancel_orders"] = "Nullable(Int64)",
                ["systime"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка статистики заявок отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(SuperCandlesOrderStats5mDTO item, string secid)
        {
            // source_time — ключевой not-null столбец (ORDER BY), собирается построчно из
            // даты и времени торгов. Пустые дата/время — отвергаем строку.
            DateTime sourceTime = MoexClickHouseTime.BuildSourceTime(item.TradeDate, item.TradeTime);

            object?[] row =
            {
                secid,
                sourceTime,
                item.PutOrdersB,
                item.PutOrdersS,
                item.PutValB,
                item.PutValS,
                item.PutVolB,
                item.PutVolS,
                item.PutVwapB,
                item.PutVwapS,
                item.PutVol,
                item.PutVal,
                item.PutOrders,
                item.CancelOrdersB,
                item.CancelOrdersS,
                item.CancelValB,
                item.CancelValS,
                item.CancelVolB,
                item.CancelVolS,
                item.CancelVwapB,
                item.CancelVwapS,
                item.CancelVol,
                item.CancelVal,
                item.CancelOrders,
                MoexClickHouseTime.AsWallClock(item.SysTime),
            };

            return (row, sourceTime);
        }
    }
}
