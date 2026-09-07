-- Внутридневной период относится к конкретному торговому дню, а не к интервалу дат.
--
-- Интервальная форма V010 предполагала, что регламент действует неизменным на
-- отрезке. Фактические расписания MOEX приходят по конкретному tradedate, а
-- исторические расписания вводятся оператором тоже по дням. Вывести день из
-- интервала нельзя, не придумав факт, поэтому таблица пересоздаётся пустой.

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM moex_trading_periods) THEN
        RAISE EXCEPTION
            'moex_trading_periods содержит строки. Дневную форму нельзя вывести из интервальной. Миграция остановлена; требуется решение владельца по существующим данным.';
    END IF;
END $$;

DROP TABLE moex_trading_periods;

CREATE TABLE moex_trading_periods (
    id               uuid        PRIMARY KEY DEFAULT uuidv7(),
    trade_date       date        NOT NULL,
    market           text        NOT NULL CHECK (market IN ('stock', 'futures')),
    boardid          text        NOT NULL DEFAULT '',
    secid            text        NOT NULL DEFAULT '',
    session          smallint,
    period_type      text        NOT NULL,
    time_from        timestamp   NOT NULL,
    time_till        timestamp,
    moex_update_time timestamp,
    data_source      text        NOT NULL CHECK (data_source IN ('calendar', 'manual')),
    note             text,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT uq_moex_trading_periods_business
        UNIQUE NULLS NOT DISTINCT
        (market, trade_date, boardid, secid, session, period_type, time_from)
);

COMMENT ON TABLE  moex_trading_periods IS
    'Внутридневные периоды конкретного торгового дня';
COMMENT ON COLUMN moex_trading_periods.secid IS
    'Инструмент; пустая строка означает правило для всех инструментов режима';
COMMENT ON COLUMN moex_trading_periods.boardid IS
    'Режим торгов; пустая строка означает правило для всех режимов рынка';
COMMENT ON COLUMN moex_trading_periods.data_source IS
    'Происхождение строки: calendar - ответ календарного API MOEX; manual - ввод оператора';
COMMENT ON COLUMN moex_trading_periods.note IS
    'Комментарий оператора; для строк calendar не заполняется';
