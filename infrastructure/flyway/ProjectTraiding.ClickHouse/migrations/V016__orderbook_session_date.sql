-- V016: Столбец торговой сессии стакану.
--
-- Задача. У фьючерсов момент снимка (из SEQNUM, поле source_time) и торговая сессия, к которой
-- биржа относит данные (из блока dataversion), на выходных торгах расходятся: снимок субботы
-- принадлежит сессии понедельника. Без trade_session_date стакан теряет эту привязку — ту же,
-- что у ленты сделок уже есть отдельным столбцом.
--
-- source_time остаётся моментом снимка. trade_session_date — торговый день по dataversion.
-- Две разные величины, подменять одну другой нельзя.
--
-- Форма как V015: ReplacingMergeTree(ingest_priority), разбиение по дню, срок жизни 30 дней.
-- Таблица пуста — пересоздание безопасно.

DROP TABLE IF EXISTS moex_orderbook;

CREATE TABLE moex_orderbook
(
    secid               LowCardinality(String),
    source_time         DateTime64(3, 'Europe/Moscow'),
    buy_sell            LowCardinality(String),
    price               Float64,
    board_id            LowCardinality(Nullable(String)),
    quantity            Nullable(Int64),
    decimals            Nullable(Int64),
    trade_session_date  Nullable(Date),
    ingest_priority     UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toDate(source_time)
ORDER BY (secid, source_time, buy_sell, price)
TTL toDateTime(source_time) + INTERVAL 30 DAY
SETTINGS non_replicated_deduplication_window = 1000;
