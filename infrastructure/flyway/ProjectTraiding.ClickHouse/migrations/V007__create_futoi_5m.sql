-- Открытый интерес (FUTOI), 5 минут. Источник: FutoiSchema / FutoiDTO.
-- secid = ticker источника. Ключевые столбцы: secid, source_time, clgroup
--   (несколько строк на момент по группам участников). Остальные Nullable.

CREATE TABLE moex_futoi_5m
(
    secid              LowCardinality(String),         -- источник: ticker
    source_time        DateTime64(3, 'Europe/Moscow'),
    clgroup            LowCardinality(String),
    sess_id            Nullable(Int32),
    seqnum             Nullable(Int32),
    pos                Nullable(Int64),
    pos_long           Nullable(Int64),
    pos_short          Nullable(Int64),
    pos_long_num       Nullable(Int64),
    pos_short_num      Nullable(Int64),
    trade_session_date Nullable(String),
    systime            Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, clgroup)
SETTINGS non_replicated_deduplication_window = 1000;
