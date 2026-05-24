-- V014: Задания на загрузку рыночных данных.
-- PK = uuid, DEFAULT uuidv7() — нативная функция PostgreSQL 18.
-- secid NOT NULL, жёсткий FK на moex_instruments.
-- candle_interval вместо interval (зарезервированное слово PostgreSQL).
-- Сценарные индексы по status и created_at — отдельной миграцией позже.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.14.

CREATE TABLE moex_load_tasks (
    id              uuid        PRIMARY KEY DEFAULT uuidv7(),
    secid           text        NOT NULL
                                REFERENCES moex_instruments (secid),
    market          text        NOT NULL CHECK (market IN ('stock', 'futures')),
    boardid         text        NOT NULL,
    data_kind       text        NOT NULL,
    candle_interval int,
    date_from       date        NOT NULL,
    date_till       date        NOT NULL,
    status          text        NOT NULL DEFAULT 'pending'
                                CHECK (status IN ('pending', 'running', 'done', 'error')),
    stop_reason     text,
    rows_loaded     bigint      NOT NULL DEFAULT 0,
    storage_target  text        NOT NULL DEFAULT 'none',
    created_at      timestamptz NOT NULL DEFAULT now(),
    started_at      timestamptz,
    finished_at     timestamptz,
    error_message   text
);

CREATE INDEX idx_moex_load_tasks_secid
    ON moex_load_tasks (secid);

COMMENT ON TABLE  moex_load_tasks IS 'Задания на загрузку рыночных данных';
COMMENT ON COLUMN moex_load_tasks.id IS 'UUIDv7: хронологическая сортировка, без фрагментации B-tree';
COMMENT ON COLUMN moex_load_tasks.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_load_tasks.data_kind IS 'candles, tradestats, obstats, orderstats, futoi, hi2, mega_alerts';
COMMENT ON COLUMN moex_load_tasks.candle_interval IS 'Интервал свечей (1, 5, 60), только для candles';
COMMENT ON COLUMN moex_load_tasks.status IS 'pending, running, done, error';
COMMENT ON COLUMN moex_load_tasks.stop_reason IS 'empty_cursor, range_exhausted, safety_cap_hit';
COMMENT ON COLUMN moex_load_tasks.storage_target IS 'none, file; clickhouse позже';
