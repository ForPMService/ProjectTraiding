-- V012: История изменений атрибутов инструментов. Cursor-пагинация.
-- secid — логическая связь с moex_instruments, FK не ставится.
-- attribute_name — логическая связь с moex_security_attributes, FK не ставится.
-- Источник: GetSecurityChanges → CalendarSecurityChangeDTO.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.12.

CREATE TABLE moex_security_changes (
    id               bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    moex_update_time timestamp   NOT NULL,
    action           text        NOT NULL CHECK (action IN ('updated', 'removed')),
    secid            text        NOT NULL,
    attribute_name   text        NOT NULL,
    before_value     text,
    after_value      text,

    UNIQUE NULLS NOT DISTINCT (
        moex_update_time,
        action,
        secid,
        attribute_name,
        before_value,
        after_value
    )
);

COMMENT ON TABLE  moex_security_changes IS 'История изменений атрибутов инструментов MOEX';
COMMENT ON COLUMN moex_security_changes.moex_update_time IS 'Время изменения, MOEX московское';
COMMENT ON COLUMN moex_security_changes.action IS 'updated или removed';
COMMENT ON COLUMN moex_security_changes.secid IS 'Тикер (логическая связь, без FK)';
COMMENT ON COLUMN moex_security_changes.attribute_name IS 'Имя атрибута (логическая связь, без FK)';
