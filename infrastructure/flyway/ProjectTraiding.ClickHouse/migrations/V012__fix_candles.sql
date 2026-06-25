-- V012: Пересоздание свечей в порядке столбцов под CandlesDTO (secid впереди).
-- Старая V001 создала таблицу в другом порядке; здесь сносим и создаём заново.
-- База пустая, данных нет — пересоздание безопасно.

DROP TABLE IF EXISTS moex_candles_1m;

CREATE TABLE moex_candles_1m
(
    secid   LowCardinality(String),
    open    Nullable(Float64),
    close   Nullable(Float64),
    high    Nullable(Float64),
    low     Nullable(Float64),
    value   Nullable(Float64),
    volume  Nullable(Float64),
    begin   DateTime64(3, 'Europe/Moscow'),
    end     Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 1000;