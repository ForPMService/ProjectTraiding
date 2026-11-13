-- Только свечные поля; ключ source_time, systime и прочая статистика остаются.
-- ВНИМАНИЕ: удаление столбцов уничтожает их прежние значения.
ALTER TABLE moex_trade_stats_5m_stock
    DROP COLUMN pr_open,
    DROP COLUMN pr_high,
    DROP COLUMN pr_low,
    DROP COLUMN pr_close,
    DROP COLUMN vol,
    DROP COLUMN val;
