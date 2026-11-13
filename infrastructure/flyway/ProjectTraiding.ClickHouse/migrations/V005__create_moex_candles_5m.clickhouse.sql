-- Новые свечи 5 минут из MOEX tradestats.
-- Схема идентична moex_candles_1m. Одна операция DDL.
-- Миграция не переносит данные и не переключает загрузчики.
CREATE TABLE moex_candles_5m
(
    secid            LowCardinality(String),
    open             Nullable(Float64),
    close            Nullable(Float64),
    high             Nullable(Float64),
    low              Nullable(Float64),
    value            Nullable(Float64),
    volume           Nullable(Float64),
    begin            DateTime64(3, 'Europe/Moscow'),
    end              Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority  UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;
