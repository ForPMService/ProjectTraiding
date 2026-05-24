-- V011: Справочник атрибутов, которые могут изменяться. ~23 строки.
-- Источник: GetSecurityAttributes → CalendarSecurityAttributeDTO.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.11.

CREATE TABLE moex_security_attributes (
    name      text PRIMARY KEY,
    data_type text NOT NULL,
    title     text NOT NULL
);

COMMENT ON TABLE  moex_security_attributes IS 'Справочник атрибутов изменений инструментов';
COMMENT ON COLUMN moex_security_attributes.name IS 'Имя атрибута';
COMMENT ON COLUMN moex_security_attributes.data_type IS 'Тип: D=date, I=integer, N=numeric, S=string, B=boolean';
COMMENT ON COLUMN moex_security_attributes.title IS 'Расшифровка атрибута';
