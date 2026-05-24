-- V010: Приостановки торгов по инструментам. До 160k+ записей.
-- reason_id — логическая связь с moex_suspension_reasons, FK не ставится.
-- secid — логическая связь с moex_instruments, FK не ставится.
-- Причина: приостановки могут содержать инструменты/причины вне справочника.
-- Источник: GetSuspended → CalendarSuspendedDTO. Cursor-пагинация.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.9.

CREATE TABLE moex_suspensions (
    id               bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    secid            text        NOT NULL,
    reason_id        text        NOT NULL,
    date_from        date        NOT NULL,
    date_till        date,
    boardid          text,
    settle_codes     text,
    change_date      date,
    moex_update_time timestamp,

    UNIQUE NULLS NOT DISTINCT (
        secid,
        reason_id,
        date_from,
        date_till,
        boardid,
        settle_codes
    )
);

COMMENT ON TABLE  moex_suspensions IS 'Приостановки торгов MOEX';
COMMENT ON COLUMN moex_suspensions.secid IS 'Тикер (логическая связь, без FK)';
COMMENT ON COLUMN moex_suspensions.reason_id IS 'Причина (логическая связь с moex_suspension_reasons, без FK)';
COMMENT ON COLUMN moex_suspensions.date_from IS 'Начало приостановки';
COMMENT ON COLUMN moex_suspensions.date_till IS 'Конец приостановки (NULL = бессрочная)';
