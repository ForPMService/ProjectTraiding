-- V014: Хранилище контура реального времени — лента сделок и стакан.
-- Источник: RealtimeTradesStockDTO (14 колонок), RealtimeTradesFuturesDTO (13),
--           RealtimeOrderbookRowDTO (8). Свечи реального времени идут в moex_candles_1m (V012).
--
-- Отступление от общего правила разбиения. У агрегатов зерно 5 минут — сотня тысяч строк
-- в год на инструмент, и годовая партиция уместна. Здесь зерно — одна сделка: у SBER больше
-- 150 000 сделок в день. Разбиение по месяцу для сделок, по дню для стакана.
--
-- Ключ строки сделки включает trade_no: замером 13.07.2026 установлено до 427 сделок
-- в одну секунду при точности времени источника в секунду. Время записи не различает.
--
-- source_time — фактический момент (TRADEDATE + TRADETIME).
-- trade_session_date — торговый день, к которому биржа отнесла сделку. У фьючерсов расходятся:
-- сделка выходного дня относится к сессии понедельника (проверено: 3770 строк из 5000).

-- ═══════════════════════════════════════════════════════════
-- Лента сделок, акции
-- ═══════════════════════════════════════════════════════════

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
    systime             Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYYYYMM(source_time)
ORDER BY (secid, source_time, trade_no)
SETTINGS non_replicated_deduplication_window = 1000;

-- ═══════════════════════════════════════════════════════════
-- Лента сделок, фьючерсы
--
-- Набор колонок иной: BOARDNAME вместо BOARDID; есть RECNO, OPENPOSITION, OFFMARKETDEAL;
-- нет VALUE, PERIOD, DECIMALS, TRADINGSESSION. Общей таблицы быть не может.
--
-- rec_no хранится, хотя в ключе избыточен (проверено: уникален и идёт в том же порядке,
-- что trade_no). Это сырое поле источника, и режим разведки данных требует хранить его как есть.
-- ═══════════════════════════════════════════════════════════

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
    systime             Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYYYYMM(source_time)
ORDER BY (secid, source_time, trade_no)
SETTINGS non_replicated_deduplication_window = 1000;

-- ═══════════════════════════════════════════════════════════
-- Стакан заявок — одна таблица на оба рынка
--
-- Структура акций и фьючерсов совпадает (8 колонок), объект передачи один
-- (RealtimeOrderbookRowDTO). Рынки различает board_id. Глубина различается — у акций
-- 10 уровней на сторону, у фьючерсов 20 — но на схему это не влияет.
--
-- source_time собирается из SEQNUM: в ответе стакана даты нет, UPDATETIME даёт лишь время
-- суток. seq_num и update_time столбцами НЕ хранятся — оба выводятся из source_time без
-- потерь: formatDateTime(source_time, '%Y%m%d%H%M%S') возвращает SEQNUM.
--
-- Ключ строки: (secid, source_time, buy_sell, price). Проверено: SEQNUM один на весь снимок,
-- цены внутри стороны не повторяются.
--
-- Разбиение по дню и срок жизни: стакан невосстановим, но объёмен — при опросе раз в 5 секунд
-- 20 инструментов дают около 5 млн строк в день. Истёкшая партиция отбрасывается целиком.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_orderbook
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    buy_sell     LowCardinality(String),
    price        Float64,
    board_id     LowCardinality(Nullable(String)),
    quantity     Nullable(Int64),
    decimals     Nullable(Int64)
)
ENGINE = MergeTree
PARTITION BY toDate(source_time)
ORDER BY (secid, source_time, buy_sell, price)
TTL toDateTime(source_time) + INTERVAL 30 DAY
SETTINGS non_replicated_deduplication_window = 1000;
