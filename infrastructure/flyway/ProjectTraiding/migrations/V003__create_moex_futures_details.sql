-- V003: Параметры фьючерса. 1:1 с moex_instruments для instrument_type = 'futures'.
-- boardid = 'RFUD' при базовой загрузке (FuturesSecurityDTO не содержит BOARDID).
-- MarketStatistics-поля nullable (обогащение позже).
-- buysell_fee, scalper_fee критичны для модели издержек.
-- Документ: MOEX_Management_Model_v0_2, раздел 5.3.

CREATE TABLE moex_futures_details (
    secid             text        PRIMARY KEY
                                  REFERENCES moex_instruments (secid),
    boardid           text        NOT NULL,
    initial_margin    numeric,
    prev_settle_price numeric,
    prev_price        numeric,
    minstep           numeric,
    stepprice         numeric,
    lotvolume         int,
    decimals          int,
    last_trade_date   date,
    last_del_date     date,
    prev_open_position bigint,
    high_limit        numeric,
    low_limit         numeric,
    -- MarketStatistics Securities (обогащение, nullable до первого вызова)
    buysell_fee       numeric,
    scalper_fee       numeric,
    last_settle_price numeric,
    settle_price_clr  numeric,
    im_time           timestamp,
    updated_at        timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_futures_details IS 'Карточка фьючерса: ГО, лимиты, комиссии';
COMMENT ON COLUMN moex_futures_details.secid IS 'Тикер фьючерса, FK → moex_instruments';
COMMENT ON COLUMN moex_futures_details.boardid IS 'Режим торгов: RFUD (константа в первом срезе)';
COMMENT ON COLUMN moex_futures_details.buysell_fee IS 'Биржевая комиссия buy/sell (MarketStatistics)';
COMMENT ON COLUMN moex_futures_details.scalper_fee IS 'Скальперская комиссия (MarketStatistics)';
COMMENT ON COLUMN moex_futures_details.im_time IS 'Время обновления ГО, MOEX московское (MarketStatistics)';
