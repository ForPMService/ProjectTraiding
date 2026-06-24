-- Статистики стакана (OBStats), фьючерсы, 5 минут.
-- Источник: AlgFuturesOrderBookSchema / SuperCandlesFuturesOrderBookStats5mDTO.
-- Ключевые столбцы: secid, source_time. Остальные (включая asset_code) Nullable.

CREATE TABLE moex_ob_stats_5m_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    mid_price    Nullable(Float64),
    micro_price  Nullable(Float64),
    spread_l1    Nullable(Float64),
    spread_l2    Nullable(Float64),
    spread_l3    Nullable(Float64),
    spread_l5    Nullable(Float64),
    spread_l10   Nullable(Float64),
    spread_l20   Nullable(Float64),
    levels_b     Nullable(Int32),
    levels_s     Nullable(Int32),
    vol_b_l1     Nullable(Int64),
    vol_b_l2     Nullable(Int64),
    vol_b_l3     Nullable(Int64),
    vol_b_l5     Nullable(Int64),
    vol_b_l10    Nullable(Int64),
    vol_b_l20    Nullable(Int64),
    vol_s_l1     Nullable(Int64),
    vol_s_l2     Nullable(Int64),
    vol_s_l3     Nullable(Int64),
    vol_s_l5     Nullable(Int64),
    vol_s_l10    Nullable(Int64),
    vol_s_l20    Nullable(Int64),
    vwap_b_l3    Nullable(Float64),
    vwap_b_l5    Nullable(Float64),
    vwap_b_l10   Nullable(Float64),
    vwap_b_l20   Nullable(Float64),
    vwap_s_l3    Nullable(Float64),
    vwap_s_l5    Nullable(Float64),
    vwap_s_l10   Nullable(Float64),
    vwap_s_l20   Nullable(Float64),
    systime      Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;
