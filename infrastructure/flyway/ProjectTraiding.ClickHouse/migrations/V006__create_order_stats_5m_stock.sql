-- Статистики заявок (OrderStats), акции, 5 минут.
-- Источник: AlgOrderStats5mSchema / SuperCandlesOrderStats5mDTO. Фьючерсного варианта нет.
-- Ключевые столбцы: secid, source_time. Остальные Nullable.
-- Неоднородность типов сохранена: cancel_vol_s/cancel_vol/cancel_orders = Int64, прочие vol = Int32.

CREATE TABLE moex_order_stats_5m_stock
(
    secid           LowCardinality(String),
    source_time     DateTime64(3, 'Europe/Moscow'),
    put_orders_b    Nullable(Int32),
    put_orders_s    Nullable(Int32),
    put_val_b       Nullable(Float64),
    put_val_s       Nullable(Float64),
    put_vol_b       Nullable(Int32),
    put_vol_s       Nullable(Int32),
    put_vwap_b      Nullable(Float64),
    put_vwap_s      Nullable(Float64),
    put_vol         Nullable(Int32),
    put_val         Nullable(Float64),
    put_orders      Nullable(Int32),
    cancel_orders_b Nullable(Int32),
    cancel_orders_s Nullable(Int32),
    cancel_val_b    Nullable(Float64),
    cancel_val_s    Nullable(Float64),
    cancel_vol_b    Nullable(Int32),
    cancel_vol_s    Nullable(Int64),
    cancel_vwap_b   Nullable(Float64),
    cancel_vwap_s   Nullable(Float64),
    cancel_vol      Nullable(Int64),
    cancel_val      Nullable(Float64),
    cancel_orders   Nullable(Int64),
    systime         Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;
