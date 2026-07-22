-- V026: Расширение списка наблюдения приёмника под свечи.
--
-- Задача. V025 завела подписки только на сделки и стакан. Приёмник растёт до полноты
-- по инструменту: добавляются свечи. Свечам, в отличие от сделок и стакана, нужен интервал.
--
-- Интервал — целочисленный код MOEX, как в moex_loaded_ranges (candle_interval int):
-- 1 — минута, 10 — десять минут, 60 — час, 24 — день. Для сделок и стакана он бессмыслен
-- и остаётся NULL.
--
-- Реальное время сейчас — только минутная свеча (interval = 1): её растущую минуту ждёт
-- график, и лишь под минуту заведена таблица приёма. Прочие интервалы добирает историческая
-- загрузка. CHECK держит контракт честным: подписаться можно лишь на то, что приёмник отдаёт.
-- Появятся таблицы и обработка старших интервалов в реальном времени — CHECK расширится тогда,
-- как ключ moex_loaded_ranges расширялся в V023 при появлении строк приёма.

ALTER TABLE moex_realtime_subscriptions
    ADD COLUMN candle_interval int NULL;

-- Имя одноколоночного CHECK предсказуемо (в отличие от усечённого имени UNIQUE) — снимаем по имени.
ALTER TABLE moex_realtime_subscriptions
    DROP CONSTRAINT moex_realtime_subscriptions_data_kind_check;

ALTER TABLE moex_realtime_subscriptions
    ADD CONSTRAINT moex_realtime_subscriptions_data_kind_check
        CHECK (data_kind IN ('trades', 'orderbook', 'candles'));

ALTER TABLE moex_realtime_subscriptions
    ADD CONSTRAINT moex_realtime_subscriptions_candle_interval_check
        CHECK (
            (data_kind IN ('trades', 'orderbook') AND candle_interval IS NULL)
            OR
            (data_kind = 'candles' AND candle_interval = 1)
        );

COMMENT ON COLUMN moex_realtime_subscriptions.candle_interval IS
    'Код интервала свечи MOEX (1 — минута). NULL для trades и orderbook. Реальное время пока только минута';