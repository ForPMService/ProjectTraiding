-- V016: Условия брокера. Ручной ввод оператором.
-- Полная модель издержек: биржевая (buysell_fee) + брокерская (эта таблица)
--   + спред (рыночные данные) + проскальзывание (minstep).
-- Сценарные индексы по market, valid_from, broker_name — отдельной миграцией позже.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.16.

CREATE TABLE moex_broker_tariffs (
    id                  bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    broker_name         text        NOT NULL,
    tariff_name         text        NOT NULL,
    market              text        NOT NULL CHECK (market IN ('stock', 'futures')),
    fee_type            text        NOT NULL,
    fee_value           numeric     NOT NULL,
    fee_currency        text        NOT NULL DEFAULT 'RUB',
    min_fee             numeric,
    turnover_threshold  numeric,
    valid_from          date        NOT NULL,
    valid_till          date,
    comment             text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_broker_tariffs IS 'Условия брокера для модели издержек';
COMMENT ON COLUMN moex_broker_tariffs.broker_name IS 'BCS, Tinkoff, Finam';
COMMENT ON COLUMN moex_broker_tariffs.tariff_name IS 'Трейдер, Инвестор';
COMMENT ON COLUMN moex_broker_tariffs.fee_type IS 'percent_of_turnover, fixed_per_contract, fixed_per_trade, monthly, depository';
COMMENT ON COLUMN moex_broker_tariffs.fee_value IS '0.01 (процент) или 3.50 (руб./контракт)';
COMMENT ON COLUMN moex_broker_tariffs.valid_from IS 'С какой даты действует';
COMMENT ON COLUMN moex_broker_tariffs.valid_till IS 'До какой даты (NULL = текущий)';
