-- Статистики стакана (OBStats), акции, 5 минут.
-- Источник: AlgOrderBookStats5mSchema / SuperCandlesOrderBookStats5mDTO.
-- Ключевые столбцы: secid, source_time. Остальные Nullable.

CREATE TABLE moex_ob_stats_5m_stock
(
    secid              LowCardinality(String),
    source_time        DateTime64(3, 'Europe/Moscow'),
    spread_bbo         Nullable(Float64),
    spread_lv10        Nullable(Float64),
    spread_1mio        Nullable(Float64),
    levels_b           Nullable(Int32),
    levels_s           Nullable(Int32),
    vol_b              Nullable(Int64),
    vol_s              Nullable(Int64),
    val_b              Nullable(Int64),
    val_s              Nullable(Int64),
    imbalance_vol_bbo  Nullable(Float64),
    imbalance_val_bbo  Nullable(Float64),
    imbalance_vol      Nullable(Float64),
    imbalance_val      Nullable(Float64),
    vwap_b             Nullable(Float64),
    vwap_s             Nullable(Float64),
    vwap_b_1mio        Nullable(Float64),
    vwap_s_1mio        Nullable(Float64),
    systime            Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;
