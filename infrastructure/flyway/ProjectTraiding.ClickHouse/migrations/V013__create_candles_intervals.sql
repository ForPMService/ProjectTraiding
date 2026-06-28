-- V013: Свечи прочих интервалов — десять минут, час, день.
-- Форма столбцов и порядок дословно как у moex_candles_1m (V012): под CandlesDTO, secid впереди.
-- Коды интервала MOEX: 10 — десять минут, 60 — час, 24 — день. Минута (1) уже есть.
-- Целевую таблицу по интервалу выбирает свечной обработчик.

CREATE TABLE moex_candles_10m
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

CREATE TABLE moex_candles_1h
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

CREATE TABLE moex_candles_1d
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