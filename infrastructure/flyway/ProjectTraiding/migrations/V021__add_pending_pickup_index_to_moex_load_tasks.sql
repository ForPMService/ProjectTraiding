-- V021: Индекс под подбор задач фоновым исполнителем.
-- Подбор ищет самую старую задачу в очереди под ClickHouse (ClaimNextPendingTaskIdAsync):
--   WHERE status IN ('pending','partial') AND storage_target='clickhouse' ORDER BY created_at.
-- Частичный индекс по created_at, ограниченный незавершёнными задачами под ClickHouse,
--   обслуживает и фильтр, и сортировку. При одной дорожке последовательное сканирование было
--   дёшево; под несколькими дорожками с частым опросом и блокировкой строк индекс оправдан.
-- В V014 этот индекс помечен как отложенный — здесь он перестаёт быть отложенным.

CREATE INDEX idx_moex_load_tasks_pickup
    ON moex_load_tasks (created_at)
    WHERE status IN ('pending', 'partial') AND storage_target = 'clickhouse';