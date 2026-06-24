-- V019: Высокие отметки приёма реального времени.
-- Одна строка на ряд: инструмент + рынок + режим торгов + вид данных + интервал свечей.
-- last_source_time — докуда принято реальное время (рыночное время, Москва);
--   строки новее этой отметки повторно не вставляются — идемпотентность по построению.
-- Долговечный источник истины приёма; Redis держит последние значения для фронтенда,
--   но истиной приёма не является.
-- Создаётся сейчас, наполняется на шаге приёмника реального времени; до тех пор пуста.
-- candle_interval вместо interval (зарезервированное слово PostgreSQL).
-- Документ: Хэндофф_слой_записи_ClickHouse v1.3, раздел 6.2.

CREATE TABLE moex_stream_cursors (
    id               bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    secid            text        NOT NULL
                                 REFERENCES moex_instruments (secid),
    market           text        NOT NULL CHECK (market IN ('stock', 'futures')),
    boardid          text        NOT NULL,
    data_kind        text        NOT NULL,
    candle_interval  int,
    last_source_time timestamptz NOT NULL,
    updated_at       timestamptz NOT NULL DEFAULT now(),

    UNIQUE NULLS NOT DISTINCT (
        secid,
        market,
        boardid,
        data_kind,
        candle_interval
    )
);

CREATE INDEX idx_moex_stream_cursors_secid
    ON moex_stream_cursors (secid);

COMMENT ON TABLE  moex_stream_cursors IS 'Высокие отметки приёма реального времени: докуда принят каждый ряд';
COMMENT ON COLUMN moex_stream_cursors.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_stream_cursors.market IS 'stock, futures';
COMMENT ON COLUMN moex_stream_cursors.boardid IS 'Режим торгов: TQBR и т.п.';
COMMENT ON COLUMN moex_stream_cursors.data_kind IS 'candles, tradestats, obstats, orderstats, futoi, hi2, mega_alerts';
COMMENT ON COLUMN moex_stream_cursors.candle_interval IS 'Интервал свечей (1, 5, 60), только для candles';
COMMENT ON COLUMN moex_stream_cursors.last_source_time IS 'Последнее принятое рыночное время (Москва). Новее — не вставляем повторно';
