-- V002: Параметры акции. 1:1 с moex_instruments для instrument_type = 'stock'.
-- Базовые поля из ISS, MarketStatistics-поля nullable (обогащение позже).
-- Источник: GetInfoTradedStockAssets → StockSecurityDTO (базовая),
--           GetMarketStatisticsStockSecuritiesAsync (обогащение).
-- Документ: MOEX_Management_Model_v0_2, раздел 5.2.

CREATE TABLE moex_stock_details (
    secid           text        PRIMARY KEY
                                REFERENCES moex_instruments (secid),
    boardid         text        NOT NULL,
    marketcode      text,
    lotsize         int,
    facevalue       numeric,
    prev_close_price numeric,
    prev_date       date,
    -- MarketStatistics Securities (обогащение, nullable до первого вызова)
    status          text,
    decimals        int,
    minstep         numeric,
    isin            text,
    currency_id     text,
    list_level      int,
    issue_size      bigint,
    settle_date     date,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_stock_details IS 'Карточка акции: параметры торгов и обогащение';
COMMENT ON COLUMN moex_stock_details.secid IS 'Тикер акции, FK → moex_instruments';
COMMENT ON COLUMN moex_stock_details.boardid IS 'Режим торгов: TQBR';
COMMENT ON COLUMN moex_stock_details.marketcode IS 'Код рынка из ISS, nullable: значение источника может отсутствовать';
COMMENT ON COLUMN moex_stock_details.lotsize IS 'Размер лота из ISS, nullable: значение источника может отсутствовать';
COMMENT ON COLUMN moex_stock_details.minstep IS 'Минимальный шаг цены (MarketStatistics)';
COMMENT ON COLUMN moex_stock_details.isin IS 'ISIN (MarketStatistics)';
COMMENT ON COLUMN moex_stock_details.list_level IS 'Уровень листинга 1/2/3 (MarketStatistics)';
COMMENT ON COLUMN moex_stock_details.issue_size IS 'Объём выпуска (MarketStatistics)';
