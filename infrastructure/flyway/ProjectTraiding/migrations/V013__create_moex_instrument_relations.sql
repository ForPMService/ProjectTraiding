-- V013: Связи между инструментами.
-- source_secid, target_secid — жёсткие FK на moex_instruments.
-- target_asset_code — без FK (asset_code нигде не PK).
-- CHECK: хотя бы один из target_secid / target_asset_code заполнен.
-- Частичный индекс по target_secid обслуживает FK-проверку.
-- Отдельный индекс на source_secid не нужен: покрыт UNIQUE.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.13.

CREATE TABLE moex_instrument_relations (
    id                bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_secid      text        NOT NULL
                                  REFERENCES moex_instruments (secid),
    target_secid      text
                                  REFERENCES moex_instruments (secid),
    target_asset_code text,
    relation_type     text        NOT NULL,
    confidence        text        NOT NULL CHECK (confidence IN ('auto', 'manual')),
    comment           text,
    created_at        timestamptz NOT NULL DEFAULT now(),

    CHECK (target_secid IS NOT NULL OR target_asset_code IS NOT NULL),

    UNIQUE NULLS NOT DISTINCT (
        source_secid,
        target_secid,
        target_asset_code,
        relation_type
    )
);

CREATE INDEX idx_moex_instrument_relations_target_secid
    ON moex_instrument_relations (target_secid)
    WHERE target_secid IS NOT NULL;

COMMENT ON TABLE  moex_instrument_relations IS 'Связи инструментов: фьючерс → базовый актив';
COMMENT ON COLUMN moex_instrument_relations.source_secid IS 'Производный инструмент (SiM6)';
COMMENT ON COLUMN moex_instrument_relations.target_secid IS 'Базовый инструмент (SBER), nullable';
COMMENT ON COLUMN moex_instrument_relations.target_asset_code IS 'Базовый актив (Si), nullable';
COMMENT ON COLUMN moex_instrument_relations.relation_type IS 'future_underlying, same_underlying, manual_related';
COMMENT ON COLUMN moex_instrument_relations.confidence IS 'auto или manual';
