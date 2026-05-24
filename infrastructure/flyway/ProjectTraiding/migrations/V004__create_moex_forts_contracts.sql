-- V004: Справочник FORTS-контрактов.
-- FK на moex_instruments не ставится: таблица содержит и неторгуемые контракты.
-- Источник истины для даты экспирации (Calendar).
-- Источник: GetFuturesSecuritiesAll → CalendarFortsContractDTO (первый проход).
-- Документ: MOEX_Management_Model_v0_2, раздел 5.4.

CREATE TABLE moex_forts_contracts (
    secid           text        PRIMARY KEY,
    asset_code      text        NOT NULL,
    shortname       text        NOT NULL,
    exec_type       text,
    contract_name   text,
    expiration_date date,
    end_date        date,
    expiration_type text,
    expiration_time time,
    weekend_session int,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_forts_contracts IS 'FORTS-контракты: экспирация и параметры';
COMMENT ON COLUMN moex_forts_contracts.secid IS 'Тикер контракта: SiM6';
COMMENT ON COLUMN moex_forts_contracts.asset_code IS 'Базовый актив: Si, BR';
COMMENT ON COLUMN moex_forts_contracts.expiration_date IS 'Дата экспирации — источник истины (Calendar)';
COMMENT ON COLUMN moex_forts_contracts.expiration_type IS 'Тип экспирации';
COMMENT ON COLUMN moex_forts_contracts.weekend_session IS 'Есть ли weekend-сессия';
