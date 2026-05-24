-- V009: Справочник причин приостановок. ~28 строк.
-- reason_id хранится как text: MOEX metadata определяет как string.
-- Загружается ДО moex_suspensions (логическая зависимость, не FK).
-- Источник: GetSuspendedReasons → CalendarSuspendedReasonDTO.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.10.

CREATE TABLE moex_suspension_reasons (
    reason_id text PRIMARY KEY,
    title     text NOT NULL
);

COMMENT ON TABLE  moex_suspension_reasons IS 'Справочник причин приостановок торгов';
COMMENT ON COLUMN moex_suspension_reasons.reason_id IS 'Идентификатор причины (text, MOEX metadata)';
COMMENT ON COLUMN moex_suspension_reasons.title IS 'Расшифровка причины';
