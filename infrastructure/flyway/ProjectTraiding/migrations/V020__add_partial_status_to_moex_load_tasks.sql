-- V020: Статус частичной загрузки.
-- partial — диапазон покрыт не полностью (например, сработал защитный предел страниц при
--   курсорной пагинации). Отличается от error (сбой источника/сети) и от done (полное покрытие).
-- Задача в статусе partial остаётся до-гружаемой: добавлена в условие claim MarkRunningAsync.
-- Имя ограничения — автоген PostgreSQL для inline-CHECK из V014; при ином имени поправить.

ALTER TABLE moex_load_tasks
    DROP CONSTRAINT IF EXISTS moex_load_tasks_status_check;

ALTER TABLE moex_load_tasks
    ADD CONSTRAINT moex_load_tasks_status_check
        CHECK (status IN ('pending', 'running', 'done', 'error', 'partial'));

COMMENT ON COLUMN moex_load_tasks.status IS 'pending, running, done, error, partial';