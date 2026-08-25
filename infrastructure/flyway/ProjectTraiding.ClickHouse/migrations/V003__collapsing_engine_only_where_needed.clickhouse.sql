-- Схлопывание нужно четырём таблицам из семнадцати: moex_candles_1m (два писателя),
-- moex_trades_stock, moex_trades_futures, moex_orderbook (перекрывающиеся окна опроса).
-- Остальные тринадцать пишет один писатель, а повторную загрузку предваряет удаление
-- диапазона. Обмен идёт через теневую таблицу: движок существующей таблицы не меняется.
--
-- ПРИМЕНЯТЬ ПРИ ОСТАНОВЛЕННОМ ИСТОРИЧЕСКОМ ЗАГРУЗЧИКЕ. Вставка или удаление, попавшие
-- в старую таблицу между INSERT ... SELECT и EXCHANGE TABLES, будут уничтожены вместе
-- с ней при DROP.
--
-- Окно дедупликации вставок у новых таблиц равно нулю, как задано V002. Значение 1000
-- из текста V001 возвращать нельзя: после удаления диапазона повторная вставка тех же
-- блоков была бы молча отброшена, и данные не записались бы вовсе.
--
-- Перенос идёт с модификатором FINAL везде, кроме двух таблиц оповещений: там ключ
-- сортировки не содержит различителя события, и FINAL мог бы удалить законные строки
-- прямо во время миграции.

DROP VIEW moex_candles_10m_final;
DROP VIEW moex_candles_1h_final;
DROP VIEW moex_candles_1d_final;
DROP VIEW moex_trade_stats_5m_stock_final;
DROP VIEW moex_trade_stats_5m_futures_final;
DROP VIEW moex_ob_stats_5m_stock_final;
DROP VIEW moex_ob_stats_5m_futures_final;
DROP VIEW moex_order_stats_5m_stock_final;
DROP VIEW moex_futoi_5m_final;
DROP VIEW moex_hi2_daily_stock_final;
DROP VIEW moex_hi2_daily_futures_final;
DROP VIEW moex_megaalerts_stock_final;
DROP VIEW moex_megaalerts_futures_final;

-- Три старших свечных интервала: ingest_priority остаётся столбцом, но перестаёт
-- быть версией схлопывания.

CREATE TABLE moex_candles_10m__new AS moex_candles_10m
ENGINE = MergeTree
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 0;
INSERT INTO moex_candles_10m__new SELECT * FROM moex_candles_10m FINAL;
EXCHANGE TABLES moex_candles_10m__new AND moex_candles_10m;
DROP TABLE moex_candles_10m__new;

CREATE TABLE moex_candles_1h__new AS moex_candles_1h
ENGINE = MergeTree
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 0;
INSERT INTO moex_candles_1h__new SELECT * FROM moex_candles_1h FINAL;
EXCHANGE TABLES moex_candles_1h__new AND moex_candles_1h;
DROP TABLE moex_candles_1h__new;

CREATE TABLE moex_candles_1d__new AS moex_candles_1d
ENGINE = MergeTree
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 0;
INSERT INTO moex_candles_1d__new SELECT * FROM moex_candles_1d FINAL;
EXCHANGE TABLES moex_candles_1d__new AND moex_candles_1d;
DROP TABLE moex_candles_1d__new;

-- Восемь статистических рядов, у которых ключ содержит измерение, исчерпывающее ряд:
-- интервал у статистик сделок, стакана и заявок, группа участников у открытого
-- интереса, показатель у концентрации. ingested_at удаляется вместе с движком.

CREATE TABLE moex_trade_stats_5m_stock__new AS moex_trade_stats_5m_stock
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_trade_stats_5m_stock__new DROP COLUMN ingested_at;
INSERT INTO moex_trade_stats_5m_stock__new
    SELECT * EXCEPT (ingested_at) FROM moex_trade_stats_5m_stock FINAL;
EXCHANGE TABLES moex_trade_stats_5m_stock__new AND moex_trade_stats_5m_stock;
DROP TABLE moex_trade_stats_5m_stock__new;

CREATE TABLE moex_trade_stats_5m_futures__new AS moex_trade_stats_5m_futures
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_trade_stats_5m_futures__new DROP COLUMN ingested_at;
INSERT INTO moex_trade_stats_5m_futures__new
    SELECT * EXCEPT (ingested_at) FROM moex_trade_stats_5m_futures FINAL;
EXCHANGE TABLES moex_trade_stats_5m_futures__new AND moex_trade_stats_5m_futures;
DROP TABLE moex_trade_stats_5m_futures__new;

CREATE TABLE moex_ob_stats_5m_stock__new AS moex_ob_stats_5m_stock
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_ob_stats_5m_stock__new DROP COLUMN ingested_at;
INSERT INTO moex_ob_stats_5m_stock__new
    SELECT * EXCEPT (ingested_at) FROM moex_ob_stats_5m_stock FINAL;
EXCHANGE TABLES moex_ob_stats_5m_stock__new AND moex_ob_stats_5m_stock;
DROP TABLE moex_ob_stats_5m_stock__new;

CREATE TABLE moex_ob_stats_5m_futures__new AS moex_ob_stats_5m_futures
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_ob_stats_5m_futures__new DROP COLUMN ingested_at;
INSERT INTO moex_ob_stats_5m_futures__new
    SELECT * EXCEPT (ingested_at) FROM moex_ob_stats_5m_futures FINAL;
EXCHANGE TABLES moex_ob_stats_5m_futures__new AND moex_ob_stats_5m_futures;
DROP TABLE moex_ob_stats_5m_futures__new;

CREATE TABLE moex_order_stats_5m_stock__new AS moex_order_stats_5m_stock
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_order_stats_5m_stock__new DROP COLUMN ingested_at;
INSERT INTO moex_order_stats_5m_stock__new
    SELECT * EXCEPT (ingested_at) FROM moex_order_stats_5m_stock FINAL;
EXCHANGE TABLES moex_order_stats_5m_stock__new AND moex_order_stats_5m_stock;
DROP TABLE moex_order_stats_5m_stock__new;

CREATE TABLE moex_futoi_5m__new AS moex_futoi_5m
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, clgroup)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_futoi_5m__new DROP COLUMN ingested_at;
INSERT INTO moex_futoi_5m__new
    SELECT * EXCEPT (ingested_at) FROM moex_futoi_5m FINAL;
EXCHANGE TABLES moex_futoi_5m__new AND moex_futoi_5m;
DROP TABLE moex_futoi_5m__new;

CREATE TABLE moex_hi2_daily_stock__new AS moex_hi2_daily_stock
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_hi2_daily_stock__new DROP COLUMN ingested_at;
INSERT INTO moex_hi2_daily_stock__new
    SELECT * EXCEPT (ingested_at) FROM moex_hi2_daily_stock FINAL;
EXCHANGE TABLES moex_hi2_daily_stock__new AND moex_hi2_daily_stock;
DROP TABLE moex_hi2_daily_stock__new;

CREATE TABLE moex_hi2_daily_futures__new AS moex_hi2_daily_futures
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_hi2_daily_futures__new DROP COLUMN ingested_at;
INSERT INTO moex_hi2_daily_futures__new
    SELECT * EXCEPT (ingested_at) FROM moex_hi2_daily_futures FINAL;
EXCHANGE TABLES moex_hi2_daily_futures__new AND moex_hi2_daily_futures;
DROP TABLE moex_hi2_daily_futures__new;

-- ВНИМАНИЕ. Две таблицы оповещений переносятся БЕЗ модификатора FINAL.
--
-- Ключ (secid, source_time, alert_type) не содержит идентификатора события: источник
-- его не отдаёт, threshold и value неключевые. Уникальность такого ключа контрактом
-- источника не гарантирована, а V001 прямо предупреждает, что натуральность ключей
-- статистической семьи на данных не проверена.
--
-- FINAL при переносе схлопнул бы совпавшие по ключу строки навсегда уже в обычном
-- движке. Обратная сторона решения известна и принята: вместе с законными строками
-- переедут физические повторы прежних перезагрузок, отличить их нельзя. Лишняя
-- строка видна и устранима перезагрузкой диапазона, утраченное событие — нет.

CREATE TABLE moex_megaalerts_stock__new AS moex_megaalerts_stock
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, alert_type)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_megaalerts_stock__new DROP COLUMN ingested_at;
INSERT INTO moex_megaalerts_stock__new
    SELECT * EXCEPT (ingested_at) FROM moex_megaalerts_stock;
EXCHANGE TABLES moex_megaalerts_stock__new AND moex_megaalerts_stock;
DROP TABLE moex_megaalerts_stock__new;

CREATE TABLE moex_megaalerts_futures__new AS moex_megaalerts_futures
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, alert_type)
SETTINGS non_replicated_deduplication_window = 0;
ALTER TABLE moex_megaalerts_futures__new DROP COLUMN ingested_at;
INSERT INTO moex_megaalerts_futures__new
    SELECT * EXCEPT (ingested_at) FROM moex_megaalerts_futures;
EXCHANGE TABLES moex_megaalerts_futures__new AND moex_megaalerts_futures;
DROP TABLE moex_megaalerts_futures__new;
