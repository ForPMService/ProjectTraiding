CREATE UNIQUE INDEX idx_moex_load_tasks_active_logical_unique
    ON moex_load_tasks (
        secid, market, boardid, data_kind, candle_interval,
        date_from, date_till, storage_target
    )
    NULLS NOT DISTINCT
    WHERE status IN ('pending', 'running', 'partial');
