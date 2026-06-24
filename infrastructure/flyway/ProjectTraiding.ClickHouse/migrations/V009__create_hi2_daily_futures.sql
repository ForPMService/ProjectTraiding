-- Концентрация (HI2), фьючерсы, ежедневно. Источник: Hi2FuturesSchema / Hi2FuturesDTO.
-- Ключевые столбцы: secid, source_time, metric. asset_code неключевой → Nullable.

CREATE TABLE moex_hi2_daily_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    metric       LowCardinality(String),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 1000;
