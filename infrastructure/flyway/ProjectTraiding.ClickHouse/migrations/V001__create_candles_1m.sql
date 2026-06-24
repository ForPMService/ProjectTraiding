-- Свечи, интервал 1 минута. Источник: AlgCandlesSchema / CandlesDTO.
-- Код инструмента (secid) подставляет загрузчик: в ответе свечей его нет.
-- Ключевые столбцы (ORDER BY) обязательны; остальные Nullable
--   (режим разведки: пустую ячейку источника храним как есть, разбираем потом).
-- Писатель проверяет на null ТОЛЬКО ключевые столбцы (secid, begin).

CREATE TABLE moex_candles_1m
(
    secid   LowCardinality(String),
    begin   DateTime64(3, 'Europe/Moscow'),
    end     Nullable(DateTime64(3, 'Europe/Moscow')),
    open    Nullable(Float64),
    high    Nullable(Float64),
    low     Nullable(Float64),
    close   Nullable(Float64),
    value   Nullable(Float64),
    volume  Nullable(Float64)
)
ENGINE = MergeTree
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 1000;
