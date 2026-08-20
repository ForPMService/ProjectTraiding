-- Календарь и события, первый этап.
--
-- Режим записи у каждой таблицы свой: у календаря дней - защищённое обновление
-- по ключу без удаления, у снимков расписания - только добавление, у остальных -
-- замена диапазона. moex_manual_events заполняет только оператор.

-- 1. Календарь дней: происхождение торговости и время работы движка.

ALTER TABLE moex_calendar_days
    ADD COLUMN data_source text NOT NULL DEFAULT 'calendar'
        CHECK (data_source IN ('calendar', 'calendar_futures', 'weekday_rule', 'observed', 'manual')),
    ADD COLUMN start_time  time,
    ADD COLUMN stop_time   time,
    ADD COLUMN note        text;

COMMENT ON COLUMN moex_calendar_days.data_source IS
    'Происхождение ТОРГОВОСТИ дня: calendar - календарь своего рынка; calendar_futures - заимствовано со срочного; weekday_rule - правило будних; observed - сверка с рядами; manual - решение оператора';
COMMENT ON COLUMN moex_calendar_days.start_time IS
    'Время работы ДВИЖКА из dailytable. Не является наблюдённой границей торгов режима. Между рынками не переносится';
COMMENT ON COLUMN moex_calendar_days.stop_time IS
    'Время работы движка из dailytable, см. start_time';
COMMENT ON COLUMN moex_calendar_days.note IS
    'Причина решения оператора или пометка сверки';

-- 2. Интервалы допуска инструмента к режиму торгов.
--
-- Интервалами, а не строкой на каждый день: вопрос "был ли допущен в дату"
-- решается попаданием в интервал, недопуск - отсутствием интервала. Листинг и
-- делистинг получаются как границы интервалов, отдельная таблица не нужна.

CREATE TABLE moex_instrument_board_intervals (
    market      text        NOT NULL CHECK (market IN ('stock', 'futures')),
    secid       text        NOT NULL,
    boardid     text        NOT NULL,
    valid_from  date        NOT NULL,
    valid_till  date,
    updated_at  timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (market, secid, boardid, valid_from)
);

COMMENT ON TABLE  moex_instrument_board_intervals IS 'Интервалы допуска инструмента к режиму торгов';
COMMENT ON COLUMN moex_instrument_board_intervals.valid_till IS
    'NULL = допуск действует. НЕ равно history_till из источника: у действующих бумаг он заполнен вчерашней датой';

CREATE INDEX ix_instrument_board_intervals_date
    ON moex_instrument_board_intervals (market, valid_from, valid_till);

-- 3. Наблюдённые границы торгового дня.
--
-- Фактические границы данных, а не объявленный регламент. Историческое
-- расписание сессий биржа не отдаёт, поэтому для прошлого границы считаются
-- по загруженным рядам. Алгоритм расчёта в этом задании не реализуется.

CREATE TABLE moex_trading_bounds_observed (
    market      text        NOT NULL CHECK (market IN ('stock', 'futures')),
    trade_date  date        NOT NULL,
    segment_no  smallint    NOT NULL,
    session     smallint,
    first_time  time        NOT NULL,
    last_time   time        NOT NULL,
    instruments int         NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (market, trade_date, segment_no)
);

COMMENT ON TABLE  moex_trading_bounds_observed IS 'Фактические границы торгового дня, вычислены по рядам';
COMMENT ON COLUMN moex_trading_bounds_observed.segment_no IS
    'Порядковый номер непрерывного отрезка торговли внутри дня';
COMMENT ON COLUMN moex_trading_bounds_observed.session IS
    'Вид сессии, если сопоставление уверенное. NULL допустим: на срочном рынке поля вида сессии нет';

-- 4. Внутридневные периоды.
--
-- Источник отдаёт расписание только на текущий день и игнорирует диапазон дат.
-- Таблица наполняется снимками и для прошлого останется пустой навсегда.
-- Снимок нельзя переснять задним числом, поэтому пишется весь ответ целиком,
-- без отбора по режиму.
--
-- Пустой secid означает "для всех инструментов режима"; на срочном рынке ту же
-- роль играет дефис. Строка с конкретным инструментом перекрывает общую.

CREATE TABLE moex_trading_periods (
    market           text        NOT NULL CHECK (market IN ('stock', 'futures')),
    trade_date       date        NOT NULL,
    snapshot_at      timestamptz NOT NULL,
    boardid          text        NOT NULL,
    secid            text        NOT NULL DEFAULT '',
    session          smallint,
    period_type      text        NOT NULL,
    time_from        timestamp   NOT NULL,
    time_till        timestamp,
    moex_update_time timestamp,
    updated_at       timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (market, trade_date, snapshot_at, boardid, secid, period_type, time_from)
);

COMMENT ON TABLE  moex_trading_periods IS 'Периоды внутри торгового дня, снимок';
COMMENT ON COLUMN moex_trading_periods.snapshot_at IS
    'Момент снятия. Отметка времени, а не дата: два снимка одних суток должны различаться';
COMMENT ON COLUMN moex_trading_periods.session IS
    'Только фондовый рынок; на срочном вид сессии задан типом периода';
COMMENT ON COLUMN moex_trading_periods.time_from IS
    'Дата со временем, а не время: срочный рынок отдаёт границы с датой, и в дни выходных сессий она значима. Для фондового рынка дата берётся из trade_date';

-- 5. Справочник типов периодов.
--
-- Наборы по рынкам пересекаются только аукционом открытия. На срочном рынке
-- аукциона закрытия нет, общую модель "у каждого рынка есть открытие и
-- закрытие" строить нельзя.

CREATE TABLE moex_trading_period_types (
    market    text NOT NULL CHECK (market IN ('stock', 'futures')),
    type_code text NOT NULL,
    title     text NOT NULL,

    PRIMARY KEY (market, type_code)
);

COMMENT ON TABLE moex_trading_period_types IS 'Типы внутридневных периодов, по рынкам';

-- 6. Экспирации срочного рынка.

CREATE TABLE moex_futures_expirations (
    secid           text        NOT NULL,
    asset_code      text,
    expiration_date date        NOT NULL,
    expiration_type text,
    end_date        date,
    weekend_session smallint,
    updated_at      timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (secid)
);

COMMENT ON TABLE  moex_futures_expirations IS 'Даты экспирации фьючерсов';
COMMENT ON COLUMN moex_futures_expirations.weekend_session IS
    'Исходное значение из MOEX без нормализации семантики';

-- 7. Дробления и консолидации.
--
-- Меняют масштаб цены и объёма. Ряд, склеенный через такую дату без поправки,
-- недостоверен, а внешнего признака в самих рядах нет.

CREATE TABLE moex_splits (
    trade_date  date        NOT NULL,
    secid       text        NOT NULL,
    before_qty  int         NOT NULL,
    after_qty   int         NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (trade_date, secid)
);

COMMENT ON TABLE moex_splits IS 'Дробления и консолидации бумаг';

-- 8. Ручные корпоративные события.
--
-- Загрузчик к таблице не обращается, её заполняет только оператор.
--
-- known_from нужен против утечки будущего: старое событие, внесённое сегодня,
-- должно считаться известным с даты объявления, а не с даты ввода.
--
-- last_trade_date - последний день покупки с дивидендом. Разрыв цены случается
-- на СЛЕДУЮЩИЙ торговый день после него, а не в дату закрытия реестра.

CREATE TABLE moex_manual_events (
    id              uuid        PRIMARY KEY DEFAULT uuidv7(),
    secid           text        NOT NULL,
    event_type      text        NOT NULL
        CHECK (event_type IN ('dividend', 'meeting', 'issue', 'delisting_announced')),
    event_date      date        NOT NULL,
    known_from      date        NOT NULL,
    record_date     date,
    last_trade_date date,
    payment_date    date,
    amount          numeric(18, 6),
    currency        text,
    source_note     text,
    created_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_manual_events IS 'Корпоративные события, вводятся оператором';
COMMENT ON COLUMN moex_manual_events.known_from IS 'С какой даты факт считается известным рынку';
COMMENT ON COLUMN moex_manual_events.last_trade_date IS
    'Последний день покупки с дивидендом; разрыв - на следующий торговый день';

CREATE INDEX ix_manual_events_secid_date ON moex_manual_events (secid, event_date);
