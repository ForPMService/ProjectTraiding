-- V017: Расчистка после перехода на InstrumentCard DTO.
--
-- 1. Дроп 8 осиротевших calendar-таблиц (парсеры и DTO перенесены в Old).
-- 2. Пересоздание moex_stock_details под StockInstrumentCardDTO.
-- 3. Пересоздание moex_futures_details под FuturesInstrumentCardDTO.
--
-- База пустая — дроп безопасен.
-- Источник решений: MOEX_Cards_Handoff_v0_1, prompt_cleanup_old_v0_1.

-- ═══════════════════════════════════════════════════════════
-- 1. Дроп осиротевших calendar-таблиц
-- ═══════════════════════════════════════════════════════════

DROP TABLE IF EXISTS moex_security_changes;      -- V012, источник: CalendarSecurityChangeDTO → Old
DROP TABLE IF EXISTS moex_security_attributes;   -- V011, источник: CalendarSecurityAttributeDTO → Old
DROP TABLE IF EXISTS moex_suspensions;            -- V010, источник: CalendarSuspendedDTO → Old
DROP TABLE IF EXISTS moex_suspension_reasons;     -- V009, источник: CalendarSuspendedReasonDTO → Old
DROP TABLE IF EXISTS moex_session_types;          -- V008, источник: CalendarSessionTypeDTO → Old
DROP TABLE IF EXISTS moex_trading_sessions;       -- V007, источник: CalendarStockSessionDTO → Old
DROP TABLE IF EXISTS moex_options_series;         -- V005, источник: CalendarOptionsSeriesDTO → Old
DROP TABLE IF EXISTS moex_forts_contracts;        -- V004, источник: CalendarFortsContractDTO → Old

-- moex_calendar_days (V006) — остаётся, источник CalendarOffDaysMarketDTO активен.

-- ═══════════════════════════════════════════════════════════
-- 2. Пересоздание moex_stock_details
--    Источник: StockInstrumentCardDTO (securities-блок, статика)
--    Ценовые поля (Last, Bid, Offer...) — live-данные, не хранятся
-- ═══════════════════════════════════════════════════════════

DROP TABLE IF EXISTS moex_stock_details;

CREATE TABLE moex_stock_details (
    secid        text        PRIMARY KEY
                             REFERENCES moex_instruments (secid),
    boardid      text        NOT NULL,
    shortname    text,
    secname      text,
    sectype      text,
    isin         text,
    lotsize      int,
    minstep      numeric,
    decimals     int,
    currency_id  text,
    issue_size   bigint,
    list_level   int,
    status       text,
    updated_at   timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_stock_details IS 'Карточка акции: статические параметры из securities-блока';
COMMENT ON COLUMN moex_stock_details.secid IS 'Тикер акции, FK → moex_instruments';
COMMENT ON COLUMN moex_stock_details.boardid IS 'Режим торгов: TQBR';
COMMENT ON COLUMN moex_stock_details.sectype IS 'Тип ценной бумаги. MOEX: SECTYPE';
COMMENT ON COLUMN moex_stock_details.isin IS 'ISIN. MOEX: ISIN';
COMMENT ON COLUMN moex_stock_details.lotsize IS 'Размер лота. MOEX: LOTSIZE';
COMMENT ON COLUMN moex_stock_details.minstep IS 'Минимальный шаг цены. MOEX: MINSTEP';
COMMENT ON COLUMN moex_stock_details.decimals IS 'Знаков после запятой. MOEX: DECIMALS';
COMMENT ON COLUMN moex_stock_details.currency_id IS 'Валюта. MOEX: CURRENCYID';
COMMENT ON COLUMN moex_stock_details.issue_size IS 'Объём выпуска. MOEX: ISSUESIZE';
COMMENT ON COLUMN moex_stock_details.list_level IS 'Уровень листинга 1/2/3. MOEX: LISTLEVEL';
COMMENT ON COLUMN moex_stock_details.status IS 'Статус торгов. MOEX: STATUS';

-- ═══════════════════════════════════════════════════════════
-- 3. Пересоздание moex_futures_details
--    Источник: FuturesInstrumentCardDTO (securities-блок, статика)
--    Ценовые поля (Last, Bid, SettlePrice...) — live-данные, не хранятся
-- ═══════════════════════════════════════════════════════════

DROP TABLE IF EXISTS moex_futures_details;

CREATE TABLE moex_futures_details (
    secid           text        PRIMARY KEY
                                REFERENCES moex_instruments (secid),
    boardid         text        NOT NULL,
    shortname       text,
    secname         text,
    asset_code      text,
    initial_margin  numeric,
    minstep         numeric,
    stepprice       numeric,
    lotvolume       int,
    decimals        int,
    last_trade_date date,
    last_del_date   date,
    high_limit      numeric,
    low_limit       numeric,
    buysell_fee     numeric,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_futures_details IS 'Карточка фьючерса: статические параметры из securities-блока';
COMMENT ON COLUMN moex_futures_details.secid IS 'Тикер фьючерса, FK → moex_instruments';
COMMENT ON COLUMN moex_futures_details.boardid IS 'Режим торгов: RFUD';
COMMENT ON COLUMN moex_futures_details.asset_code IS 'Код базового актива: Si, BR. MOEX: ASSETCODE';
COMMENT ON COLUMN moex_futures_details.initial_margin IS 'Гарантийное обеспечение. MOEX: INITIALMARGIN';
COMMENT ON COLUMN moex_futures_details.minstep IS 'Минимальный шаг цены. MOEX: MINSTEP';
COMMENT ON COLUMN moex_futures_details.stepprice IS 'Стоимость шага цены. MOEX: STEPPRICE';
COMMENT ON COLUMN moex_futures_details.lotvolume IS 'Размер лота. MOEX: LOTVOLUME';
COMMENT ON COLUMN moex_futures_details.decimals IS 'Знаков после запятой. MOEX: DECIMALS';
COMMENT ON COLUMN moex_futures_details.last_trade_date IS 'Последний день торгов. MOEX: LASTTRADEDATE';
COMMENT ON COLUMN moex_futures_details.last_del_date IS 'Дата исполнения. MOEX: LASTDELDATE';
COMMENT ON COLUMN moex_futures_details.high_limit IS 'Верхний лимит цены. MOEX: HIGHLIMIT';
COMMENT ON COLUMN moex_futures_details.low_limit IS 'Нижний лимит цены. MOEX: LOWLIMIT';
COMMENT ON COLUMN moex_futures_details.buysell_fee IS 'Комиссия за сделку. MOEX: BUYSELLFEE';
