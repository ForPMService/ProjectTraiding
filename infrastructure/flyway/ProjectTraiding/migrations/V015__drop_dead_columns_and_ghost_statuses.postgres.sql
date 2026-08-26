-- Четыре столбца задания и покрытия пишутся и не читаются: версии договора и писателя
-- переносят постоянные значения, счётчик попыток только растёт, число загруженных строк
-- дублирует rows_total покрытия, причина остановки после снятия держателя исхода имеет
-- единственное значение у всех успешных заданий.
--
-- Статусы partial и stale не присваиваются нигде: читающий схему решил бы, что механизм
-- пометки неполного покрытия существует. Его нет — история снимает строку целиком или
-- режет на остатки.
--
-- Индекс покрытия приёма не имеет потребителя: запроса расчёта дыр в приложении нет,
-- а поддерживается он при каждом сердцебиении каждого активного ряда.
--
-- Порядок обязателен: сначала данные, затем ограничения, затем индексы, затем столбцы.

-- 1. Покрытие со статусами-призраками удаляется, а не переводится в ok: полнота таких
--    диапазонов неизвестна, и перевод записал бы в журнал непроверенное утверждение.
DELETE FROM moex_loaded_ranges WHERE status IN ('partial', 'stale');

-- 2. Задания в partial переводятся в error, а не удаляются: на них ссылается покрытие
--    через last_task_id.
UPDATE moex_load_tasks SET status = 'error' WHERE status = 'partial';

-- 3. Ограничения статусов.
ALTER TABLE moex_loaded_ranges
    DROP CONSTRAINT moex_loaded_ranges_status_check;
ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT moex_loaded_ranges_status_check
        CHECK (status IN ('ok', 'open', 'closed', 'crashed'));

ALTER TABLE moex_load_tasks
    DROP CONSTRAINT moex_load_tasks_status_check;
ALTER TABLE moex_load_tasks
    ADD CONSTRAINT moex_load_tasks_status_check
        CHECK (status IN ('pending', 'running', 'done', 'error', 'cancelled'));

-- 4. Уникальность активных заданий без partial. Индекс подбирается для ON CONFLICT
--    по логическому следованию: предикат индекса обязан вытекать из предиката запроса.
--    Массовая постановка уже называет только pending и running (коммит 2), поэтому
--    сужение индекса до тех же двух статусов её обслуживает.
DROP INDEX idx_moex_load_tasks_active_logical_unique;
CREATE UNIQUE INDEX idx_moex_load_tasks_active_logical_unique
    ON moex_load_tasks (
        secid, market, boardid, data_kind, candle_interval,
        date_from, date_till, storage_target
    )
    NULLS NOT DISTINCT
    WHERE status IN ('pending', 'running');

-- 5. Индекс покрытия приёма без потребителей.
DROP INDEX idx_moex_loaded_ranges_stream_span;

-- 6. Столбцы.
ALTER TABLE moex_load_tasks    DROP COLUMN source_contract_version;
ALTER TABLE moex_load_tasks    DROP COLUMN writer_version;
ALTER TABLE moex_load_tasks    DROP COLUMN attempt_count;
ALTER TABLE moex_load_tasks    DROP COLUMN rows_loaded;
ALTER TABLE moex_load_tasks    DROP COLUMN stop_reason;
ALTER TABLE moex_loaded_ranges DROP COLUMN source_contract_version;
ALTER TABLE moex_loaded_ranges DROP COLUMN writer_version;

-- 7. Комментарии к статусам перечисляли снятые значения.
COMMENT ON COLUMN moex_load_tasks.status IS NULL;
COMMENT ON COLUMN moex_loaded_ranges.status IS NULL;
