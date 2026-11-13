-- Чтение свечей 5 минут с учетом ReplacingMergeTree.
CREATE VIEW moex_candles_5m_final AS SELECT * FROM moex_candles_5m FINAL;
