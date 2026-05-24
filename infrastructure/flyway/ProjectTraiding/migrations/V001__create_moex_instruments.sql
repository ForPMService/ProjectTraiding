-- V001: Единый справочник инструментов MOEX.
-- FK-цель для большинства остальных таблиц.
-- Источник: GetInfoTradedStockAssets → StockSecurityDTO,
--           GetInfoTradedFuturesAssets → FuturesSecurityDTO.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.1.

CREATE TABLE moex_instruments (
    secid           text        PRIMARY KEY,
    instrument_type text        NOT NULL CHECK (instrument_type IN ('stock', 'futures')),
    asset_code      text,
    shortname       text        NOT NULL,
    secname         text        NOT NULL,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_instruments IS 'Единый справочник инструментов MOEX';
COMMENT ON COLUMN moex_instruments.secid IS 'Тикер: SBER, SiM6';
COMMENT ON COLUMN moex_instruments.instrument_type IS 'stock или futures';
COMMENT ON COLUMN moex_instruments.asset_code IS 'Базовый актив (Si, BR) — только для фьючерсов';
COMMENT ON COLUMN moex_instruments.shortname IS 'Короткое название';
COMMENT ON COLUMN moex_instruments.secname IS 'Полное название';
COMMENT ON COLUMN moex_instruments.updated_at IS 'Системное: когда строка обновлена';
