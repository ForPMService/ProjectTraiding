-- Столбец описывает выбор, которого нет: значение одно, валидатор принимал одно,
-- V013 сузила ограничение до одного. Он занимает место в двух объектах уникальности
-- и в предикате индекса подбора, ничего при этом не различая.
--
-- Ограничения и индексы пересоздаются ДО удаления столбца намеренно. Автоматическое
-- каскадное удаление сняло бы уникальность вместе со столбцом, а она нужна и без него.
--
-- Столбец константен по всем строкам (V013 это гарантирует ограничением), поэтому
-- исключение его из ключей новых дублей не создаёт.

-- 1. Ограничения допустимых значений.
ALTER TABLE moex_load_tasks
    DROP CONSTRAINT chk_moex_load_tasks_storage_target;

ALTER TABLE moex_loaded_ranges
    DROP CONSTRAINT chk_moex_loaded_ranges_storage_target;

-- 2. Индекс подбора: предикат называл целевое хранилище.
DROP INDEX idx_moex_load_tasks_pickup;
CREATE INDEX idx_moex_load_tasks_pickup
    ON moex_load_tasks (created_at)
    WHERE status = 'pending';

-- 3. Уникальность активных заданий. Набор столбцов обязан совпадать с целью конфликта
--    в массовой постановке задач.
DROP INDEX idx_moex_load_tasks_active_logical_unique;
CREATE UNIQUE INDEX idx_moex_load_tasks_active_logical_unique
    ON moex_load_tasks (
        secid, market, boardid, data_kind, candle_interval,
        date_from, date_till
    )
    NULLS NOT DISTINCT
    WHERE status IN ('pending', 'running');

-- 4. Уникальность покрытия. Имя сохраняется: на него ссылается ON CONFLICT ON CONSTRAINT
--    при нарезке остатков.
ALTER TABLE moex_loaded_ranges
    DROP CONSTRAINT uq_moex_loaded_ranges_span;

ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT uq_moex_loaded_ranges_span
        UNIQUE NULLS NOT DISTINCT (
            secid,
            market,
            boardid,
            data_kind,
            candle_interval,
            date_from,
            date_till,
            time_from,
            time_till
        );

-- 5. Столбцы.
ALTER TABLE moex_load_tasks    DROP COLUMN storage_target;
ALTER TABLE moex_loaded_ranges DROP COLUMN storage_target;
