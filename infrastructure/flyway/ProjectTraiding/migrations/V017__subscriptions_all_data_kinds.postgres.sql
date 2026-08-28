-- Подписка становится общим признаком того, что инструмент отслеживается.
-- Периодическая загрузка текущего дня читает тот же список подписок для всех
-- поддерживаемых видов данных.

ALTER TABLE moex_realtime_subscriptions
    DROP CONSTRAINT moex_realtime_subscriptions_data_kind_check;

ALTER TABLE moex_realtime_subscriptions
    DROP CONSTRAINT moex_realtime_subscriptions_candle_interval_check;

ALTER TABLE moex_realtime_subscriptions
    ADD CONSTRAINT moex_realtime_subscriptions_data_kind_check
        CHECK (data_kind IN (
            'trades',
            'orderbook',
            'candles',
            'tradestats',
            'obstats',
            'orderstats',
            'futoi',
            'mega_alerts',
            'hi2'
        ));

ALTER TABLE moex_realtime_subscriptions
    ADD CONSTRAINT moex_realtime_subscriptions_candle_interval_check
        CHECK (
            (data_kind = 'candles' AND candle_interval = 1)
            OR
            (data_kind <> 'candles' AND candle_interval IS NULL)
        );
