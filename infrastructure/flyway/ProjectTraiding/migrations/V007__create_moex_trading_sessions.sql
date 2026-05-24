-- V007: Расписание торговых сессий.
-- UNIQUE NULLS NOT DISTINCT для дедупликации и ON CONFLICT.
-- secid nullable — не FK (сессии могут быть общие для рынка).
-- boardid nullable — у фьючерсов может прийти как "-".
-- Пустой secid и "-" в secid нормализуются в NULL при записи.
-- Пустой boardid и "-" в boardid тоже нормализуются в NULL.
-- Stock: time_from собирается из TradeDate + TimeFrom (time).
-- Futures: TimeFrom уже DateTime?, пишется напрямую как timestamp.
-- Источник: GetStockSessionWithTypes / GetFuturesSessionWithTypes.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.7.

CREATE TABLE moex_trading_sessions (
    id               bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    market           text        NOT NULL CHECK (market IN ('stock', 'futures')),
    session_date     date        NOT NULL,
    trading_session  int,
    boardid          text,
    secid            text,
    session_type     text        NOT NULL,
    time_from        timestamp   NOT NULL,
    time_till        timestamp,
    moex_update_time timestamp,

    UNIQUE NULLS NOT DISTINCT (
        market,
        session_date,
        boardid,
        secid,
        session_type,
        time_from
    )
);

COMMENT ON TABLE  moex_trading_sessions IS 'Расписание торговых сессий MOEX';
COMMENT ON COLUMN moex_trading_sessions.market IS 'stock или futures';
COMMENT ON COLUMN moex_trading_sessions.secid IS 'Тикер (NULL = общая сессия для рынка)';
COMMENT ON COLUMN moex_trading_sessions.session_type IS 'Тип сессии (расшифровка в moex_session_types)';
COMMENT ON COLUMN moex_trading_sessions.time_from IS 'Начало сессии, MOEX московское время';
