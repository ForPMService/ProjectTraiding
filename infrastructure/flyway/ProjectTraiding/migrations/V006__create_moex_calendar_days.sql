-- V006: Торговый календарь: выходные, праздники, weekend-сессии.
-- Одна таблица на оба рынка (stock, futures).
-- Хранит только нерабочие и особые дни, не полный календарь.
-- Источник: GetStockOffDays / GetFuturesOffDays → CalendarOffDaysMarketDTO.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.6.

CREATE TABLE moex_calendar_days (
    trade_date       date        NOT NULL,
    market           text        NOT NULL CHECK (market IN ('stock', 'futures')),
    is_traded        int         NOT NULL,
    trade_session_date date,
    reason           text,
    moex_update_time timestamp,
    updated_at       timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (trade_date, market)
);

COMMENT ON TABLE  moex_calendar_days IS 'Календарь: нерабочие и особые дни MOEX';
COMMENT ON COLUMN moex_calendar_days.trade_date IS 'Дата';
COMMENT ON COLUMN moex_calendar_days.market IS 'stock или futures';
COMMENT ON COLUMN moex_calendar_days.is_traded IS '1 = торги есть, 0 = нет';
COMMENT ON COLUMN moex_calendar_days.reason IS 'H = праздник, W = выходной с weekend-сессией';
