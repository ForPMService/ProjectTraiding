-- Статистики сделок (TradeStats), акции, 5 минут.
-- Источник: AlgCandlesTradeStatSchema / SuperCandlesTradeStats5mDTO.
-- Ключевые столбцы: secid, source_time. Остальные Nullable.
-- source_time собирается писателем из tradedate + tradetime.

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
    systime      Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;
