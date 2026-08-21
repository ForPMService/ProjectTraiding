-- Регламент рынка и режима действует на интервале дат, а не является снимком.
--
-- Таблица пуста: программных потребителей снимка больше нет. Поэтому миграция
-- меняет форму напрямую, не пытаясь вывести интервал действия из старых снимков.

ALTER TABLE moex_trading_periods
    DROP CONSTRAINT moex_trading_periods_pkey;

ALTER TABLE moex_trading_periods
    DROP COLUMN snapshot_at;

ALTER TABLE moex_trading_periods
    RENAME COLUMN trade_date TO valid_from;

ALTER TABLE moex_trading_periods
    ADD COLUMN valid_till date NOT NULL;

ALTER TABLE moex_trading_periods
    ADD CONSTRAINT ck_moex_trading_periods_valid_range
        CHECK (valid_from <= valid_till),
    ADD PRIMARY KEY (market, valid_from, valid_till, boardid, secid, period_type, time_from);

COMMENT ON TABLE moex_trading_periods IS
    'Периоды внутридневного регламента, действующие на интервале дат';
COMMENT ON COLUMN moex_trading_periods.valid_from IS
    'Первый день действия регламента';
COMMENT ON COLUMN moex_trading_periods.valid_till IS
    'Последний день действия регламента';
