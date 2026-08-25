-- MODIFY SETTING is idempotent and does not touch data. Partial application leaves a harmless mix
-- of 0 and 1000 values; after a failure run flyway repair, then repeat this migration.
ALTER TABLE moex_candles_1m           MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_candles_10m          MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_candles_1h           MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_candles_1d           MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_trades_stock         MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_trades_futures       MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_orderbook            MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_trade_stats_5m_stock    MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_trade_stats_5m_futures  MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_ob_stats_5m_stock       MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_ob_stats_5m_futures     MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_order_stats_5m_stock    MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_futoi_5m                MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_hi2_daily_stock         MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_hi2_daily_futures       MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_megaalerts_stock        MODIFY SETTING non_replicated_deduplication_window = 0;
ALTER TABLE moex_megaalerts_futures      MODIFY SETTING non_replicated_deduplication_window = 0;
