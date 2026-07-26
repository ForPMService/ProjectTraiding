-- ═══════════════════════════════════════════════════════════════════════════
-- V001: Начальная схема ProjectTraiding (PostgreSQL).
--
-- Свёртка прежней цепочки V001–V026 в одно итоговое состояние. Прежние файлы
-- удалены; история их применения жила в flyway_schema_history и снесена вместе
-- с базой.
--
-- Существовавшие данные (справочник инструментов, подписки, курсоры приёма,
-- покрытие, задачи загрузки) удалены намеренно. Перенос в новую начальную схему
-- не выполняется: справочник наполняется заново синхронизацией с биржей,
-- рыночные ряды — исторической загрузкой.
--
-- Что вошло и откуда:
--   moex_instruments           — прежняя V001, без изменений
--   moex_stock_details         — прежняя V017 (пересоздана под InstrumentCard)
--   moex_futures_details       — прежняя V017 (пересоздана под InstrumentCard)
--   moex_calendar_days         — прежняя V006, без изменений
--   moex_instrument_relations  — прежняя V013, без изменений
--   moex_broker_tariffs        — прежняя V016, без изменений
--   moex_load_tasks            — прежние V014 + V018 + V020 + V021 + V022
--   moex_loaded_ranges         — прежние V015 + V018 + V023
--   moex_stream_cursors        — прежние V019 + V024
--   moex_realtime_subscriptions— прежние V025 + V026
--
-- Что НЕ вошло: восемь календарных таблиц, снесённых прежней V017
--   (moex_forts_contracts, moex_options_series, moex_trading_sessions,
--    moex_session_types, moex_suspension_reasons, moex_suspensions,
--    moex_security_attributes, moex_security_changes). Их разборщики и объекты
--   передачи данных перенесены в Old — таблицы осиротели и удалены намеренно.
--
-- Порядок операторов подчинён внешним ключам: справочник инструментов первым,
-- moex_loaded_ranges после moex_load_tasks (ссылается на него).
-- ═══════════════════════════════════════════════════════════════════════════


-- ═══════════════════════════════════════════════════════════
-- 1. Справочник инструментов — цель внешних ключей остальных таблиц
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_instruments (
    secid           text        PRIMARY KEY,
    instrument_type text        NOT NULL CHECK (instrument_type IN ('stock', 'futures')),
    asset_code      text,
    shortname       text        NOT NULL,
    secname         text        NOT NULL,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_instruments IS 'Единый справочник инструментов MOEX';
COMMENT ON COLUMN moex_instruments.secid IS 'Тикер: SBER, SiM6';
COMMENT ON COLUMN moex_instruments.instrument_type IS 'stock или futures';
COMMENT ON COLUMN moex_instruments.asset_code IS 'Базовый актив (Si, BR) — только для фьючерсов';
COMMENT ON COLUMN moex_instruments.shortname IS 'Короткое название';
COMMENT ON COLUMN moex_instruments.secname IS 'Полное название';
COMMENT ON COLUMN moex_instruments.updated_at IS 'Системное: когда строка обновлена';


-- ═══════════════════════════════════════════════════════════
-- 2. Карточка акции
--    Источник: StockInstrumentCardDTO (блок securities, статика).
--    Ценовые поля (Last, Bid, Offer) — данные реального времени, не хранятся.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_stock_details (
    secid        text        PRIMARY KEY
                             REFERENCES moex_instruments (secid),
    boardid      text        NOT NULL,
    shortname    text,
    secname      text,
    sectype      text,
    isin         text,
    lotsize      int,
    minstep      numeric,
    decimals     int,
    currency_id  text,
    issue_size   bigint,
    list_level   int,
    status       text,
    updated_at   timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_stock_details IS 'Карточка акции: статические параметры из securities-блока';
COMMENT ON COLUMN moex_stock_details.secid IS 'Тикер акции, FK → moex_instruments';
COMMENT ON COLUMN moex_stock_details.boardid IS 'Режим торгов: TQBR';
COMMENT ON COLUMN moex_stock_details.sectype IS 'Тип ценной бумаги. MOEX: SECTYPE';
COMMENT ON COLUMN moex_stock_details.isin IS 'ISIN. MOEX: ISIN';
COMMENT ON COLUMN moex_stock_details.lotsize IS 'Размер лота. MOEX: LOTSIZE';
COMMENT ON COLUMN moex_stock_details.minstep IS 'Минимальный шаг цены. MOEX: MINSTEP';
COMMENT ON COLUMN moex_stock_details.decimals IS 'Знаков после запятой. MOEX: DECIMALS';
COMMENT ON COLUMN moex_stock_details.currency_id IS 'Валюта. MOEX: CURRENCYID';
COMMENT ON COLUMN moex_stock_details.issue_size IS 'Объём выпуска. MOEX: ISSUESIZE';
COMMENT ON COLUMN moex_stock_details.list_level IS 'Уровень листинга 1/2/3. MOEX: LISTLEVEL';
COMMENT ON COLUMN moex_stock_details.status IS 'Статус торгов. MOEX: STATUS';


-- ═══════════════════════════════════════════════════════════
-- 3. Карточка фьючерса
--    Источник: FuturesInstrumentCardDTO (блок securities, статика).
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_futures_details (
    secid           text        PRIMARY KEY
                                REFERENCES moex_instruments (secid),
    boardid         text        NOT NULL,
    shortname       text,
    secname         text,
    asset_code      text,
    initial_margin  numeric,
    minstep         numeric,
    stepprice       numeric,
    lotvolume       int,
    decimals        int,
    last_trade_date date,
    last_del_date   date,
    high_limit      numeric,
    low_limit       numeric,
    buysell_fee     numeric,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_futures_details IS 'Карточка фьючерса: статические параметры из securities-блока';
COMMENT ON COLUMN moex_futures_details.secid IS 'Тикер фьючерса, FK → moex_instruments';
COMMENT ON COLUMN moex_futures_details.boardid IS 'Режим торгов: RFUD';
COMMENT ON COLUMN moex_futures_details.asset_code IS 'Код базового актива: Si, BR. MOEX: ASSETCODE';
COMMENT ON COLUMN moex_futures_details.initial_margin IS 'Гарантийное обеспечение. MOEX: INITIALMARGIN';
COMMENT ON COLUMN moex_futures_details.minstep IS 'Минимальный шаг цены. MOEX: MINSTEP';
COMMENT ON COLUMN moex_futures_details.stepprice IS 'Стоимость шага цены. MOEX: STEPPRICE';
COMMENT ON COLUMN moex_futures_details.lotvolume IS 'Размер лота. MOEX: LOTVOLUME';
COMMENT ON COLUMN moex_futures_details.decimals IS 'Знаков после запятой. MOEX: DECIMALS';
COMMENT ON COLUMN moex_futures_details.last_trade_date IS 'Последний день торгов. MOEX: LASTTRADEDATE';
COMMENT ON COLUMN moex_futures_details.last_del_date IS 'Дата исполнения. MOEX: LASTDELDATE';
COMMENT ON COLUMN moex_futures_details.high_limit IS 'Верхний лимит цены. MOEX: HIGHLIMIT';
COMMENT ON COLUMN moex_futures_details.low_limit IS 'Нижний лимит цены. MOEX: LOWLIMIT';
COMMENT ON COLUMN moex_futures_details.buysell_fee IS 'Комиссия за сделку. MOEX: BUYSELLFEE';


-- ═══════════════════════════════════════════════════════════
-- 4. Торговый календарь: выходные, праздники, выходные сессии.
--    Хранит только нерабочие и особые дни, не полный календарь.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_calendar_days (
    trade_date         date        NOT NULL,
    market             text        NOT NULL CHECK (market IN ('stock', 'futures')),
    is_traded          int         NOT NULL,
    trade_session_date date,
    reason             text,
    moex_update_time   timestamp,
    updated_at         timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (trade_date, market)
);

COMMENT ON TABLE  moex_calendar_days IS 'Календарь: нерабочие и особые дни MOEX';
COMMENT ON COLUMN moex_calendar_days.trade_date IS 'Дата';
COMMENT ON COLUMN moex_calendar_days.market IS 'stock или futures';
COMMENT ON COLUMN moex_calendar_days.is_traded IS '1 = торги есть, 0 = нет';
COMMENT ON COLUMN moex_calendar_days.reason IS 'H = праздник, W = выходной с выходной сессией';


-- ═══════════════════════════════════════════════════════════
-- 5. Связи инструментов: фьючерс → базовый актив.
--    target_asset_code без внешнего ключа: asset_code нигде не первичный.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_instrument_relations (
    id                bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_secid      text        NOT NULL
                                  REFERENCES moex_instruments (secid),
    target_secid      text
                                  REFERENCES moex_instruments (secid),
    target_asset_code text,
    relation_type     text        NOT NULL,
    confidence        text        NOT NULL CHECK (confidence IN ('auto', 'manual')),
    comment           text,
    created_at        timestamptz NOT NULL DEFAULT now(),

    CHECK (target_secid IS NOT NULL OR target_asset_code IS NOT NULL),

    UNIQUE NULLS NOT DISTINCT (
        source_secid,
        target_secid,
        target_asset_code,
        relation_type
    )
);

CREATE INDEX idx_moex_instrument_relations_target_secid
    ON moex_instrument_relations (target_secid)
    WHERE target_secid IS NOT NULL;

COMMENT ON TABLE  moex_instrument_relations IS 'Связи инструментов: фьючерс → базовый актив';
COMMENT ON COLUMN moex_instrument_relations.source_secid IS 'Производный инструмент (SiM6)';
COMMENT ON COLUMN moex_instrument_relations.target_secid IS 'Базовый инструмент (SBER), nullable';
COMMENT ON COLUMN moex_instrument_relations.target_asset_code IS 'Базовый актив (Si), nullable';
COMMENT ON COLUMN moex_instrument_relations.relation_type IS 'future_underlying, same_underlying, manual_related';
COMMENT ON COLUMN moex_instrument_relations.confidence IS 'auto или manual';


-- ═══════════════════════════════════════════════════════════
-- 6. Условия брокера. Ручной ввод оператором.
--    Полная модель издержек: биржевая комиссия + брокерская (эта таблица)
--    + спред (рыночные данные) + проскальзывание (minstep).
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_broker_tariffs (
    id                 bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    broker_name        text        NOT NULL,
    tariff_name        text        NOT NULL,
    market             text        NOT NULL CHECK (market IN ('stock', 'futures')),
    fee_type           text        NOT NULL,
    fee_value          numeric     NOT NULL,
    fee_currency       text        NOT NULL DEFAULT 'RUB',
    min_fee            numeric,
    turnover_threshold numeric,
    valid_from         date        NOT NULL,
    valid_till         date,
    comment            text,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_broker_tariffs IS 'Условия брокера для модели издержек';
COMMENT ON COLUMN moex_broker_tariffs.broker_name IS 'BCS, Tinkoff, Finam';
COMMENT ON COLUMN moex_broker_tariffs.tariff_name IS 'Трейдер, Инвестор';
COMMENT ON COLUMN moex_broker_tariffs.fee_type IS 'percent_of_turnover, fixed_per_contract, fixed_per_trade, monthly, depository';
COMMENT ON COLUMN moex_broker_tariffs.fee_value IS '0.01 (процент) или 3.50 (руб./контракт)';
COMMENT ON COLUMN moex_broker_tariffs.valid_from IS 'С какой даты действует';
COMMENT ON COLUMN moex_broker_tariffs.valid_till IS 'До какой даты (NULL = текущий)';


-- ═══════════════════════════════════════════════════════════
-- 7. Задания на загрузку рыночных данных.
--
--    Первичный ключ — uuidv7: хронологическая сортировка без фрагментации.
--    Версии смысла загрузки и писателя входят в токен дедупликации ClickHouse.
--    Аудиторный токен истиной не является и в уникальности не участвует.
--
--    Уникальность логическая и частичная: она запрещает вторую НЕЗАВЕРШЁННУЮ
--    задачу на тот же ряд и диапазон, но разрешает повторную загрузку после
--    завершения. Ей соответствует уточнение конфликта в массовой постановке
--    задач (ProjectTraiding.Management) — там указан тот же предикат по статусу.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_load_tasks (
    id              uuid        PRIMARY KEY DEFAULT uuidv7(),
    secid           text        NOT NULL
                                REFERENCES moex_instruments (secid),
    market          text        NOT NULL CHECK (market IN ('stock', 'futures')),
    boardid         text        NOT NULL,
    data_kind       text        NOT NULL,
    candle_interval int,
    date_from       date        NOT NULL,
    date_till       date        NOT NULL,
    status          text        NOT NULL DEFAULT 'pending',
    stop_reason     text,
    rows_loaded     bigint      NOT NULL DEFAULT 0,
    storage_target  text        NOT NULL DEFAULT 'none',
    created_at      timestamptz NOT NULL DEFAULT now(),
    started_at      timestamptz,
    finished_at     timestamptz,
    error_message   text,

    source_contract_version         text NOT NULL DEFAULT 'moex_history_v1',
    writer_version                  text NOT NULL DEFAULT 'clickhouse_writer_v1',
    attempt_count                   int  NOT NULL DEFAULT 0,
    last_insert_deduplication_token text,

    CONSTRAINT moex_load_tasks_status_check
        CHECK (status IN ('pending', 'running', 'done', 'error', 'partial')),
    CONSTRAINT chk_moex_load_tasks_storage_target
        CHECK (storage_target IN ('none', 'file', 'clickhouse'))
);

CREATE INDEX idx_moex_load_tasks_secid
    ON moex_load_tasks (secid);

-- Подбор задач фоновым исполнителем: самая старая задача в статусе pending под ClickHouse.
-- Подтверждено кодом ClaimNextPendingTaskIdAsync: подбор берёт РОВНО один статус (pending),
-- не два. Частичный индекс обслуживает и фильтр, и сортировку.
-- Статус partial из системы не исчез: он остаётся в уникальном индексе ниже и в списке
-- допустимых статусов, потому что частичная задача — живая единица. Её просто возвращает
-- в работу другой путь, а не этот подбор.
CREATE INDEX idx_moex_load_tasks_pickup
    ON moex_load_tasks (created_at)
    WHERE status = 'pending' AND storage_target = 'clickhouse';

-- Защита от дублей задач: только среди незавершённых.
CREATE UNIQUE INDEX idx_moex_load_tasks_active_logical_unique
    ON moex_load_tasks (
        secid, market, boardid, data_kind, candle_interval,
        date_from, date_till, storage_target
    )
    NULLS NOT DISTINCT
    WHERE status IN ('pending', 'running', 'partial');

COMMENT ON TABLE  moex_load_tasks IS 'Задания на загрузку рыночных данных';
COMMENT ON COLUMN moex_load_tasks.id IS 'UUIDv7: хронологическая сортировка, без фрагментации B-tree';
COMMENT ON COLUMN moex_load_tasks.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_load_tasks.data_kind IS 'candles, tradestats, obstats, orderstats, futoi, hi2, mega_alerts';
COMMENT ON COLUMN moex_load_tasks.candle_interval IS 'Интервал свечей MOEX (1, 10, 60, 24), только для candles';
COMMENT ON COLUMN moex_load_tasks.status IS 'pending, running, done, error, partial';
COMMENT ON COLUMN moex_load_tasks.stop_reason IS 'empty_cursor, range_exhausted, safety_cap_hit';
COMMENT ON COLUMN moex_load_tasks.storage_target IS 'none, file, clickhouse';
COMMENT ON COLUMN moex_load_tasks.source_contract_version IS 'Версия смысла загрузки: схема/зона/нормализация/версия разборщика. Входит в токен дедупликации';
COMMENT ON COLUMN moex_load_tasks.writer_version IS 'Версия писателя ClickHouse. Входит в токен дедупликации';
COMMENT ON COLUMN moex_load_tasks.attempt_count IS 'Число попыток загрузки этой единицы';
COMMENT ON COLUMN moex_load_tasks.last_insert_deduplication_token IS 'Аудит: фактически отправленный в ClickHouse токен. НЕ источник истины, в уникальности не участвует';


-- ═══════════════════════════════════════════════════════════
-- 8. Покрытие: загруженные диапазоны истории И сеансы приёма.
--
--    Два рода строк в одной таблице:
--      история — точность суточная, time_from и time_till пусты, границы несёт
--                пара date_from / date_till, статусы ok / partial / stale;
--      приём   — заполнены обе пары, статусы open / closed / crashed, за один
--                день по одному ряду строк столько, сколько было сеансов.
--
--    Признак «строка приёма» производный: time_from IS NOT NULL. Отдельным
--    столбцом не хранится — производные не хранятся столбцами.
--
--    Ключ уникальности включает временные границы. У истории они пусты, и
--    NULLS NOT DISTINCT сохраняет прежнее поведение: историческая единица
--    «инструмент-ряд-диапазон» остаётся единственной.
--
--    ВНИМАНИЕ для писателя истории (MoexLoadedRangeWriter). Уточнение конфликта
--    в UPSERT задаётся ИМЕНЕМ ограничения:
--
--        ON CONFLICT ON CONSTRAINT uq_moex_loaded_ranges_span DO UPDATE SET ...
--
--    Перечисление столбцов тоже работает, но лишь если перечислены ВСЕ десять,
--    включая time_from и time_till: PostgreSQL выводит целевой индекс по точному
--    совпадению набора столбцов, подмножество индекс не находит и запрос падает
--    с ошибкой 42P10. Имя ограничения короче и не разъедется при изменении
--    порядка столбцов.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_loaded_ranges (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    secid           text        NOT NULL
                                REFERENCES moex_instruments (secid),
    market          text        NOT NULL CHECK (market IN ('stock', 'futures')),
    boardid         text        NOT NULL,
    data_kind       text        NOT NULL,
    candle_interval int,
    date_from       date        NOT NULL,
    date_till       date        NOT NULL,
    last_success_at timestamptz NOT NULL DEFAULT now(),
    last_task_id    uuid
                                REFERENCES moex_load_tasks (id),
    rows_total      bigint      NOT NULL DEFAULT 0,
    storage_target  text        NOT NULL DEFAULT 'none',
    status          text        NOT NULL DEFAULT 'ok',

    source_contract_version         text NOT NULL DEFAULT 'moex_history_v1',
    writer_version                  text NOT NULL DEFAULT 'clickhouse_writer_v1',
    last_insert_deduplication_token text,

    time_from       timestamptz,
    time_till       timestamptz,

    CONSTRAINT moex_loaded_ranges_status_check
        CHECK (status IN ('ok', 'partial', 'stale', 'open', 'closed', 'crashed')),

    CONSTRAINT chk_moex_loaded_ranges_storage_target
        CHECK (storage_target IN ('none', 'file', 'clickhouse')),

    -- Времена идут парой: либо обоих нет (история), либо есть оба (приём).
    CONSTRAINT chk_moex_loaded_ranges_time_pair
        CHECK (
            (time_from IS NULL AND time_till IS NULL)
            OR
            (time_from IS NOT NULL AND time_till IS NOT NULL)
        ),

    -- Границы не вывернуты. При NULL проверка даёт NULL и строку пропускает.
    CONSTRAINT chk_moex_loaded_ranges_time_order
        CHECK (time_till >= time_from),

    CONSTRAINT chk_moex_loaded_ranges_date_order
        CHECK (date_till >= date_from),

    -- Статусы приёма невозможны у строки без временных границ.
    CONSTRAINT chk_moex_loaded_ranges_stream_status
        CHECK (
            status NOT IN ('open', 'closed', 'crashed')
            OR time_from IS NOT NULL
        ),

    CONSTRAINT uq_moex_loaded_ranges_span
        UNIQUE NULLS NOT DISTINCT (
            secid,
            market,
            boardid,
            data_kind,
            candle_interval,
            date_from,
            date_till,
            storage_target,
            time_from,
            time_till
        )
);

CREATE INDEX idx_moex_loaded_ranges_last_task_id
    ON moex_loaded_ranges (last_task_id)
    WHERE last_task_id IS NOT NULL;

-- Запрос при старте процесса: какие строки остались открытыми после падения.
CREATE INDEX idx_moex_loaded_ranges_open
    ON moex_loaded_ranges (secid, data_kind)
    WHERE status = 'open';

-- Запрос расчёта дыр: строки приёма по инструменту и ряду, упорядоченные по времени.
CREATE INDEX idx_moex_loaded_ranges_stream_span
    ON moex_loaded_ranges (secid, data_kind, candle_interval, time_from)
    WHERE time_from IS NOT NULL;

COMMENT ON TABLE  moex_loaded_ranges IS 'Покрытие: загруженные диапазоны истории и сеансы приёма реального времени';
COMMENT ON COLUMN moex_loaded_ranges.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_loaded_ranges.last_task_id IS 'Последнее задание, FK → moex_load_tasks (nullable)';
COMMENT ON COLUMN moex_loaded_ranges.storage_target IS 'none = проверено но не сохранено, file, clickhouse';
COMMENT ON COLUMN moex_loaded_ranges.status IS 'История: ok, partial, stale. Приём: open, closed, crashed';
COMMENT ON COLUMN moex_loaded_ranges.time_from IS 'Начало сеанса приёма (рыночное время). NULL у исторических строк — их точность суточная';
COMMENT ON COLUMN moex_loaded_ranges.time_till IS 'Конец сеанса приёма. Двигается сердцебиением, замирает при разрыве. NULL у исторических строк';
COMMENT ON COLUMN moex_loaded_ranges.source_contract_version IS 'Версия смысла загрузки. Входит в токен дедупликации';
COMMENT ON COLUMN moex_loaded_ranges.writer_version IS 'Версия писателя. Входит в токен дедупликации';
COMMENT ON COLUMN moex_loaded_ranges.last_insert_deduplication_token IS 'Аудит: фактически отправленный токен. НЕ источник истины';


-- ═══════════════════════════════════════════════════════════
-- 9. Высокие отметки приёма реального времени.
--
--    Одна строка на ряд. Отметка движется только вперёд и по построению
--    не способна выразить разрыв — журналом покрытия она не является,
--    покрытие ведёт moex_loaded_ranges. Здесь только идемпотентность вставки.
--
--    last_trade_no заполняется лишь для ленты сделок: биржа принимает курсор
--    по номеру сделки, а не по времени, и до 427 сделок приходится на одну
--    секунду при точности времени источника в секунду.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_stream_cursors (
    id               bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    secid            text        NOT NULL
                                 REFERENCES moex_instruments (secid),
    market           text        NOT NULL CHECK (market IN ('stock', 'futures')),
    boardid          text        NOT NULL,
    data_kind        text        NOT NULL,
    candle_interval  int,
    last_source_time timestamptz NOT NULL,
    last_trade_no    bigint,
    updated_at       timestamptz NOT NULL DEFAULT now(),

    UNIQUE NULLS NOT DISTINCT (
        secid,
        market,
        boardid,
        data_kind,
        candle_interval
    )
);

CREATE INDEX idx_moex_stream_cursors_secid
    ON moex_stream_cursors (secid);

COMMENT ON TABLE  moex_stream_cursors IS 'Высокие отметки приёма реального времени: докуда принят каждый ряд';
COMMENT ON COLUMN moex_stream_cursors.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_stream_cursors.market IS 'stock, futures';
COMMENT ON COLUMN moex_stream_cursors.boardid IS 'Режим торгов: TQBR и т.п.';
COMMENT ON COLUMN moex_stream_cursors.data_kind IS 'История: candles, tradestats, obstats, orderstats, futoi, hi2, mega_alerts. Приём: trades, orderbook, candles';
COMMENT ON COLUMN moex_stream_cursors.candle_interval IS 'Интервал свечей MOEX (1, 10, 60, 24), только для candles';
COMMENT ON COLUMN moex_stream_cursors.last_source_time IS 'Последнее принятое рыночное время (Москва). Новее — не вставляем повторно';
COMMENT ON COLUMN moex_stream_cursors.last_trade_no IS 'Номер последней принятой сделки (TRADENO). Только для ленты сделок; у стакана и свечей NULL. Курсор биржи: tradeno + next_trade=1';


-- ═══════════════════════════════════════════════════════════
-- 10. Список наблюдения приёмника реального времени.
--
--     Справочник отвечает на вопрос «какие инструменты известны», подписки —
--     на вопрос «какие сейчас разрешено собирать». Это разные вопросы.
--     Рынок здесь НЕ хранится: он свойство инструмента и берётся соединением.
--
--     Зерно строки — пара инструмент + вид данных. Пустой список означает
--     пустой приём: если строк нет, приёмник не опрашивает ничего.
--
--     Реальное время сейчас только минутная свеча. Ограничение держит контракт
--     честным: подписаться можно лишь на то, что приёмник умеет отдавать.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_realtime_subscriptions (
    secid           text        NOT NULL
                                REFERENCES moex_instruments (secid),
    data_kind       text        NOT NULL,
    candle_interval int         NULL,
    enabled         boolean     NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (secid, data_kind),

    CONSTRAINT moex_realtime_subscriptions_data_kind_check
        CHECK (data_kind IN ('trades', 'orderbook', 'candles')),

    CONSTRAINT moex_realtime_subscriptions_candle_interval_check
        CHECK (
            (data_kind IN ('trades', 'orderbook') AND candle_interval IS NULL)
            OR
            (data_kind = 'candles' AND candle_interval = 1)
        )
);

COMMENT ON TABLE  moex_realtime_subscriptions IS 'Список наблюдения приёмника: какие инструменты и виды данных собирать';
COMMENT ON COLUMN moex_realtime_subscriptions.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_realtime_subscriptions.data_kind IS 'trades, orderbook, candles — включаются независимо';
COMMENT ON COLUMN moex_realtime_subscriptions.candle_interval IS 'Код интервала свечи MOEX (1 — минута). NULL для trades и orderbook. Реальное время пока только минута';
COMMENT ON COLUMN moex_realtime_subscriptions.enabled IS 'false — временно снять с наблюдения, не удаляя строку. Применяется после перезапуска приёмника';
COMMENT ON COLUMN moex_realtime_subscriptions.updated_at IS 'Обновляется при каждом изменении строки; команды Management обязаны ставить updated_at = now() при UPDATE';
