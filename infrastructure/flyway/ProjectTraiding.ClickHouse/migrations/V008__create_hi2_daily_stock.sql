-- Концентрация (HI2), акции, ежедневно. Источник: Hi2AssetSchema / Hi2AssetDTO.
-- Узкая форма (строка на показатель). Ключевые столбцы: secid, source_time, metric.
-- metric ключевой → обязателен; писатель отвергает строку с пустым metric.

CREATE TABLE moex_hi2_daily_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    metric       LowCardinality(String),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 1000;
