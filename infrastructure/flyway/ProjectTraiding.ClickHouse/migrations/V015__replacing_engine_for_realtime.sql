-- V015: Перевод таблиц приёмника реального времени на ReplacingMergeTree.
--
-- Задача. Приёмник при перезапуске может записать строки повторно (щель между вставкой и
-- сдвигом курсора). Токен дедупликации по содержимому пачки не спасает: повторная пачка
-- отличается от первой, потому что рынок за время сбоя наторговал ещё. ReplacingMergeTree
-- схлопывает дубли по ключу сортировки при слиянии — ключи всех таблиц уникальны по смыслу.
--
-- Версия схлопывания — ingest_priority: при равном ключе побеждает бо́льшее значение.
-- История свечей пишет 1, приёмник пишет 0 — историческая свеча перекрывает свеже-принятую.
-- У ленты сделок и стакана источник один, столбец всегда 0, добавлен ради единообразия.
--
-- Чтение: результат без дублей требует модификатора FINAL. До фонового слияния обычный SELECT
-- видит обе строки-дубликата. FINAL — обязанность читателей (витрина, аналитика), не приёмника.
--
-- Данные стёрты скриптом очистки (свечи) либо ещё не писались (сделки, стакан). Форма как V012.

-- ═══════════════════════════════════════════════════════════
-- Свечи минутные: два писателя (история и приём), версия решает столкновение
-- ═══════════════════════════════════════════════════════════

DROP TABLE IF EXISTS moex_candles_1m;

CREATE TABLE moex_candles_1m
(
    secid            LowCardinality(String),
    open             Nullable(Float64),
    close            Nullable(Float64),
    high             Nullable(Float64),
    low              Nullable(Float64),
    value            Nullable(Float64),
    volume           Nullable(Float64),
    begin            DateTime64(3, 'Europe/Moscow'),
    end              Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority  UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 1000;

-- ═══════════════════════════════════════════════════════════
-- Лента сделок, акции
-- ═══════════════════════════════════════════════════════════

DROP TABLE IF EXISTS moex_trades_stock;

CREATE TABLE moex_trades_stock
(
    secid               LowCardinality(String),
    source_time         DateTime64(3, 'Europe/Moscow'),
    trade_no            Int64,
    board_id            LowCardinality(Nullable(String)),
    price               Nullable(Float64),
    quantity            Nullable(Int64),
    value               Nullable(Float64),
    period              LowCardinality(Nullable(String)),
    buy_sell            LowCardinality(Nullable(String)),
    decimals            Nullable(Int32),
    trading_session     LowCardinality(Nullable(String)),
    trade_session_date  Nullable(Date),
    systime             Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority     UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYYYYMM(source_time)
ORDER BY (secid, source_time, trade_no)
SETTINGS non_replicated_deduplication_window = 1000;

-- ═══════════════════════════════════════════════════════════
-- Лента сделок, фьючерсы
-- ═══════════════════════════════════════════════════════════

DROP TABLE IF EXISTS moex_trades_futures;

CREATE TABLE moex_trades_futures
(
    secid               LowCardinality(String),
    source_time         DateTime64(3, 'Europe/Moscow'),
    trade_no            Int64,
    board_name          LowCardinality(Nullable(String)),
    price               Nullable(Float64),
    quantity            Nullable(Int64),
    rec_no              Nullable(Int64),
    open_position       Nullable(Int64),
    off_market_deal     Nullable(Int32),
    buy_sell            LowCardinality(Nullable(String)),
    trade_session_date  Nullable(Date),
    systime             Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority     UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYYYYMM(source_time)
ORDER BY (secid, source_time, trade_no)
SETTINGS non_replicated_deduplication_window = 1000;

-- ═══════════════════════════════════════════════════════════
-- Стакан заявок, оба рынка
-- ═══════════════════════════════════════════════════════════

DROP TABLE IF EXISTS moex_orderbook;

CREATE TABLE moex_orderbook
(
    secid            LowCardinality(String),
    source_time      DateTime64(3, 'Europe/Moscow'),
    buy_sell         LowCardinality(String),
    price            Float64,
    board_id         LowCardinality(Nullable(String)),
    quantity         Nullable(Int64),
    decimals         Nullable(Int64),
    ingest_priority  UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toDate(source_time)
ORDER BY (secid, source_time, buy_sell, price)
TTL toDateTime(source_time) + INTERVAL 30 DAY
SETTINGS non_replicated_deduplication_window = 1000;