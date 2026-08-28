-- Схлопывание возвращается всем семнадцати таблицам: у рядов ALGOPACK появляется
-- второй постоянный писатель — периодическая загрузка текущего дня.
--
-- Миграция уничтожает данные: тринадцать таблиц удаляются и создаются заново.
-- Применять только на хранилище, содержимое которого не требуется сохранять.
--
-- Окно дедупликации вставок остаётся нулевым, как задано V002. Значение 1000
-- из текста V001 возвращать нельзя: после удаления диапазона повторная вставка
-- тех же блоков была бы молча отброшена.

DROP TABLE moex_candles_10m;

CREATE TABLE moex_candles_10m
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
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_candles_1h;

CREATE TABLE moex_candles_1h
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
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_candles_1d;

CREATE TABLE moex_candles_1d
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
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_trade_stats_5m_stock;

CREATE TABLE moex_trade_stats_5m_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    pr_open      Nullable(Float64),
    pr_high      Nullable(Float64),
    pr_low       Nullable(Float64),
    pr_close     Nullable(Float64),
    pr_std       Nullable(Float64),
    vol          Nullable(Int32),
    val          Nullable(Float64),
    trades       Nullable(Int32),
    pr_vwap      Nullable(Float64),
    pr_change    Nullable(Float64),
    trades_b     Nullable(Int32),
    trades_s     Nullable(Int32),
    val_b        Nullable(Float64),
    val_s        Nullable(Float64),
    vol_b        Nullable(Int64),
    vol_s        Nullable(Int64),
    disb         Nullable(Float64),
    pr_vwap_b    Nullable(Float64),
    pr_vwap_s    Nullable(Float64),
    sec_pr_open  Nullable(Int32),
    sec_pr_high  Nullable(Int32),
    sec_pr_low   Nullable(Int32),
    sec_pr_close Nullable(Int32),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_trade_stats_5m_futures;

CREATE TABLE moex_trade_stats_5m_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    pr_open      Nullable(Float64),
    pr_high      Nullable(Float64),
    pr_low       Nullable(Float64),
    pr_close     Nullable(Float64),
    pr_std       Nullable(Float64),
    vol          Nullable(Int64),
    val          Nullable(Int64),
    trades       Nullable(Int32),
    pr_vwap      Nullable(Float64),
    pr_change    Nullable(Float64),
    trades_b     Nullable(Int32),
    trades_s     Nullable(Int32),
    val_b        Nullable(Float64),
    val_s        Nullable(Float64),
    vol_b        Nullable(Int64),
    vol_s        Nullable(Int64),
    disb         Nullable(Float64),
    pr_vwap_b    Nullable(Float64),
    pr_vwap_s    Nullable(Float64),
    im           Nullable(Float64),
    oi_open      Nullable(Int64),
    oi_high      Nullable(Int64),
    oi_low       Nullable(Int64),
    oi_close     Nullable(Int64),
    sec_pr_open  Nullable(Int32),
    sec_pr_high  Nullable(Int32),
    sec_pr_low   Nullable(Int32),
    sec_pr_close Nullable(Int32),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_ob_stats_5m_stock;

CREATE TABLE moex_ob_stats_5m_stock
(
    secid              LowCardinality(String),
    source_time        DateTime64(3, 'Europe/Moscow'),
    spread_bbo         Nullable(Float64),
    spread_lv10        Nullable(Float64),
    spread_1mio        Nullable(Float64),
    levels_b           Nullable(Int32),
    levels_s           Nullable(Int32),
    vol_b              Nullable(Int64),
    vol_s              Nullable(Int64),
    val_b              Nullable(Int64),
    val_s              Nullable(Int64),
    imbalance_vol_bbo  Nullable(Float64),
    imbalance_val_bbo  Nullable(Float64),
    imbalance_vol      Nullable(Float64),
    imbalance_val      Nullable(Float64),
    vwap_b             Nullable(Float64),
    vwap_s             Nullable(Float64),
    vwap_b_1mio        Nullable(Float64),
    vwap_s_1mio        Nullable(Float64),
    systime            Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at        DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_ob_stats_5m_futures;

CREATE TABLE moex_ob_stats_5m_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    mid_price    Nullable(Float64),
    micro_price  Nullable(Float64),
    spread_l1    Nullable(Float64),
    spread_l2    Nullable(Float64),
    spread_l3    Nullable(Float64),
    spread_l5    Nullable(Float64),
    spread_l10   Nullable(Float64),
    spread_l20   Nullable(Float64),
    levels_b     Nullable(Int32),
    levels_s     Nullable(Int32),
    vol_b_l1     Nullable(Int64),
    vol_b_l2     Nullable(Int64),
    vol_b_l3     Nullable(Int64),
    vol_b_l5     Nullable(Int64),
    vol_b_l10    Nullable(Int64),
    vol_b_l20    Nullable(Int64),
    vol_s_l1     Nullable(Int64),
    vol_s_l2     Nullable(Int64),
    vol_s_l3     Nullable(Int64),
    vol_s_l5     Nullable(Int64),
    vol_s_l10    Nullable(Int64),
    vol_s_l20    Nullable(Int64),
    vwap_b_l3    Nullable(Float64),
    vwap_b_l5    Nullable(Float64),
    vwap_b_l10   Nullable(Float64),
    vwap_b_l20   Nullable(Float64),
    vwap_s_l3    Nullable(Float64),
    vwap_s_l5    Nullable(Float64),
    vwap_s_l10   Nullable(Float64),
    vwap_s_l20   Nullable(Float64),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_order_stats_5m_stock;

CREATE TABLE moex_order_stats_5m_stock
(
    secid           LowCardinality(String),
    source_time     DateTime64(3, 'Europe/Moscow'),
    put_orders_b    Nullable(Int32),
    put_orders_s    Nullable(Int32),
    put_val_b       Nullable(Float64),
    put_val_s       Nullable(Float64),
    put_vol_b       Nullable(Int32),
    put_vol_s       Nullable(Int32),
    put_vwap_b      Nullable(Float64),
    put_vwap_s      Nullable(Float64),
    put_vol         Nullable(Int32),
    put_val         Nullable(Float64),
    put_orders      Nullable(Int32),
    cancel_orders_b Nullable(Int32),
    cancel_orders_s Nullable(Int32),
    cancel_val_b    Nullable(Float64),
    cancel_val_s    Nullable(Float64),
    cancel_vol_b    Nullable(Int32),
    cancel_vol_s    Nullable(Int64),
    cancel_vwap_b   Nullable(Float64),
    cancel_vwap_s   Nullable(Float64),
    cancel_vol      Nullable(Int64),
    cancel_val      Nullable(Float64),
    cancel_orders   Nullable(Int64),
    systime         Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at     DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_futoi_5m;

CREATE TABLE moex_futoi_5m
(
    secid              LowCardinality(String),
    source_time        DateTime64(3, 'Europe/Moscow'),
    clgroup            LowCardinality(String),
    sess_id            Nullable(Int32),
    seqnum             Nullable(Int32),
    pos                Nullable(Int64),
    pos_long           Nullable(Int64),
    pos_short          Nullable(Int64),
    pos_long_num       Nullable(Int64),
    pos_short_num      Nullable(Int64),
    trade_session_date Nullable(String),
    systime            Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at        DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, clgroup)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_hi2_daily_stock;

CREATE TABLE moex_hi2_daily_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    metric       LowCardinality(String),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_hi2_daily_futures;

CREATE TABLE moex_hi2_daily_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    metric       LowCardinality(String),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_megaalerts_stock;

CREATE TABLE moex_megaalerts_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    alert_type   LowCardinality(String),
    threshold    Nullable(Float64),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, alert_type)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

DROP TABLE moex_megaalerts_futures;

CREATE TABLE moex_megaalerts_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    alert_type   LowCardinality(String),
    threshold    Nullable(Float64),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, alert_type)
SETTINGS non_replicated_deduplication_window = 0,
         min_age_to_force_merge_seconds = 3600,
         min_age_to_force_merge_on_partition_only = 0;

CREATE VIEW moex_candles_10m_final             AS SELECT * FROM moex_candles_10m FINAL;
CREATE VIEW moex_candles_1h_final              AS SELECT * FROM moex_candles_1h FINAL;
CREATE VIEW moex_candles_1d_final              AS SELECT * FROM moex_candles_1d FINAL;
CREATE VIEW moex_trade_stats_5m_stock_final    AS SELECT * FROM moex_trade_stats_5m_stock FINAL;
CREATE VIEW moex_trade_stats_5m_futures_final  AS SELECT * FROM moex_trade_stats_5m_futures FINAL;
CREATE VIEW moex_ob_stats_5m_stock_final       AS SELECT * FROM moex_ob_stats_5m_stock FINAL;
CREATE VIEW moex_ob_stats_5m_futures_final     AS SELECT * FROM moex_ob_stats_5m_futures FINAL;
CREATE VIEW moex_order_stats_5m_stock_final    AS SELECT * FROM moex_order_stats_5m_stock FINAL;
CREATE VIEW moex_futoi_5m_final                AS SELECT * FROM moex_futoi_5m FINAL;
CREATE VIEW moex_hi2_daily_stock_final         AS SELECT * FROM moex_hi2_daily_stock FINAL;
CREATE VIEW moex_hi2_daily_futures_final       AS SELECT * FROM moex_hi2_daily_futures FINAL;
CREATE VIEW moex_megaalerts_stock_final        AS SELECT * FROM moex_megaalerts_stock FINAL;
CREATE VIEW moex_megaalerts_futures_final      AS SELECT * FROM moex_megaalerts_futures FINAL;

ALTER TABLE moex_candles_1m     MODIFY SETTING min_age_to_force_merge_seconds = 3600, min_age_to_force_merge_on_partition_only = 0;
ALTER TABLE moex_trades_stock   MODIFY SETTING min_age_to_force_merge_seconds = 3600, min_age_to_force_merge_on_partition_only = 0;
ALTER TABLE moex_trades_futures MODIFY SETTING min_age_to_force_merge_seconds = 3600, min_age_to_force_merge_on_partition_only = 0;
ALTER TABLE moex_orderbook      MODIFY SETTING min_age_to_force_merge_seconds = 3600, min_age_to_force_merge_on_partition_only = 1;

ALTER TABLE moex_orderbook REMOVE TTL;
