-- V015: Непрерывные успешно загруженные диапазоны.
-- secid — жёсткий FK на moex_instruments (покрыт UNIQUE, ведущая колонка).
-- last_task_id — жёсткий FK на moex_load_tasks, nullable.
-- Частичный индекс по last_task_id обслуживает FK-проверку.
-- storage_target = 'none': данные не сохранены, не для расчёта признаков.
-- candle_interval вместо interval (зарезервированное слово PostgreSQL).
-- Документ: MOEX_Management_Model_v0_2, раздел 5.15.

CREATE TABLE moex_loaded_ranges (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    secid           text        NOT NULL
                                REFERENCES moex_instruments (secid),
    market          text        NOT NULL CHECK (market IN ('stock', 'futures')),
    boardid         text        NOT NULL,
    data_kind       text        NOT NULL,
    candle_interval int,
    date_from       date        NOT NULL,
    date_till       date        NOT NULL,
    last_success_at timestamptz NOT NULL DEFAULT now(),
    last_task_id    uuid
                                REFERENCES moex_load_tasks (id),
    rows_total      bigint      NOT NULL DEFAULT 0,
    storage_target  text        NOT NULL DEFAULT 'none',
    status          text        NOT NULL DEFAULT 'ok'
                                CHECK (status IN ('ok', 'partial', 'stale')),

    UNIQUE NULLS NOT DISTINCT (
        secid,
        market,
        boardid,
        data_kind,
        candle_interval,
        date_from,
        date_till,
        storage_target
    )
);

CREATE INDEX idx_moex_loaded_ranges_last_task_id
    ON moex_loaded_ranges (last_task_id)
    WHERE last_task_id IS NOT NULL;

COMMENT ON TABLE  moex_loaded_ranges IS 'Загруженные диапазоны рыночных данных';
COMMENT ON COLUMN moex_loaded_ranges.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_loaded_ranges.last_task_id IS 'Последнее задание, FK → moex_load_tasks (nullable)';
COMMENT ON COLUMN moex_loaded_ranges.storage_target IS 'none = проверено но не сохранено, file, clickhouse';
COMMENT ON COLUMN moex_loaded_ranges.status IS 'ok, partial, stale';
