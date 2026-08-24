-- Валидатор принимает единственное значение storage_target. Схема приводится
-- к тому же виду: значения none и file в проекте не пишет никто.

DO $$
DECLARE
    bad_tasks  bigint;
    bad_ranges bigint;
BEGIN
    SELECT count(*) INTO bad_tasks
    FROM moex_load_tasks
    WHERE storage_target <> 'clickhouse';

    SELECT count(*) INTO bad_ranges
    FROM moex_loaded_ranges
    WHERE storage_target <> 'clickhouse';

    IF bad_tasks > 0 OR bad_ranges > 0 THEN
        RAISE EXCEPTION
            'V013 остановлена: moex_load_tasks содержит % строк, moex_loaded_ranges — % строк со storage_target, отличным от clickhouse. Устраните строки и повторите.',
            bad_tasks, bad_ranges;
    END IF;
END $$;

ALTER TABLE moex_load_tasks
    DROP CONSTRAINT IF EXISTS chk_moex_load_tasks_storage_target;

ALTER TABLE moex_load_tasks
    ADD CONSTRAINT chk_moex_load_tasks_storage_target
        CHECK (storage_target = 'clickhouse');

ALTER TABLE moex_load_tasks
    ALTER COLUMN storage_target SET DEFAULT 'clickhouse';

ALTER TABLE moex_loaded_ranges
    DROP CONSTRAINT IF EXISTS chk_moex_loaded_ranges_storage_target;

ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT chk_moex_loaded_ranges_storage_target
        CHECK (storage_target = 'clickhouse');

ALTER TABLE moex_loaded_ranges
    ALTER COLUMN storage_target SET DEFAULT 'clickhouse';

COMMENT ON COLUMN moex_load_tasks.storage_target IS 'clickhouse';
COMMENT ON COLUMN moex_loaded_ranges.storage_target IS 'clickhouse';
