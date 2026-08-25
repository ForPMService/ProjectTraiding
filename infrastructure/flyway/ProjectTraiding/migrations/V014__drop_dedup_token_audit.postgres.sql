ALTER TABLE moex_load_tasks    DROP COLUMN last_insert_deduplication_token;
ALTER TABLE moex_loaded_ranges DROP COLUMN last_insert_deduplication_token;

-- Четыре комментария к столбцам версий утверждают, что те входят в токен
-- дедупликации. Токена больше нет, верного утверждения на их место нет.
COMMENT ON COLUMN moex_load_tasks.source_contract_version    IS NULL;
COMMENT ON COLUMN moex_load_tasks.writer_version             IS NULL;
COMMENT ON COLUMN moex_loaded_ranges.source_contract_version IS NULL;
COMMENT ON COLUMN moex_loaded_ranges.writer_version          IS NULL;
