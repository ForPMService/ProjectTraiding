-- Календарный день содержит только факт источника.
--
-- start_time/stop_time были временем работы движка из dailytable, а не границами
-- торговой сессии. Внутридневная структура живёт в moex_trading_periods, поэтому
-- в календарном дне эти колонки вводили в заблуждение.
--
-- Происхождение торговости сокращается до двух значений. calendar_futures и
-- weekday_rule обозначали домысленное значение; загрузчик их больше не пишет.
-- observed не писал никто: писателя у этого значения не существовало.

ALTER TABLE moex_calendar_days
    DROP COLUMN start_time,
    DROP COLUMN stop_time;

-- Строки с домысленной торговостью удаляются, а не переписываются: заменить
-- их можно только повторной загрузкой из источника.

DELETE FROM moex_calendar_days
    WHERE data_source IN ('calendar_futures', 'weekday_rule', 'observed');

ALTER TABLE moex_calendar_days
    DROP CONSTRAINT moex_calendar_days_data_source_check;

ALTER TABLE moex_calendar_days
    ADD CONSTRAINT moex_calendar_days_data_source_check
        CHECK (data_source IN ('calendar', 'manual'));

COMMENT ON COLUMN moex_calendar_days.data_source IS
    'Происхождение торговости дня: calendar - факт календаря своего рынка; manual - решение оператора';
