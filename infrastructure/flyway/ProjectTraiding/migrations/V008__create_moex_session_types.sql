-- V008: Справочник типов торговых сессий. ~10-20 строк.
-- Источник: GetStockSessionWithTypes / GetFuturesSessionWithTypes (второй проход).
-- Документ: MOEX_Management_Model_v0_2, раздел 5.8.

CREATE TABLE moex_session_types (
    type_code text NOT NULL,
    market    text NOT NULL CHECK (market IN ('stock', 'futures')),
    title     text NOT NULL,

    PRIMARY KEY (type_code, market)
);

COMMENT ON TABLE  moex_session_types IS 'Справочник типов сессий MOEX';
COMMENT ON COLUMN moex_session_types.type_code IS 'Код типа сессии';
COMMENT ON COLUMN moex_session_types.market IS 'stock или futures';
COMMENT ON COLUMN moex_session_types.title IS 'Расшифровка: Основная торговая сессия, Аукцион открытия...';
