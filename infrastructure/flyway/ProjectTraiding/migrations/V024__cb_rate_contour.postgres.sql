-- Контур ключевой ставки: журнал фактов заседания и дневной календарь ставки.
--
-- Обе таблицы ведёт оператор. Система не загружает источники и не рассчитывает
-- значения: rate_announced и rate_effective календаря вводятся руками и из
-- журнала не выводятся.
--
-- Цикл заседания связывает строки через cycle_id. Идентификатор передаёт
-- оператор со страницы; если он не передан, система выдаёт новый.
--
-- Отдельного вида факта для отмены нет. Признак is_cancelled относится к
-- строке: цикл считается закрытым, если у него есть хотя бы одна строка с
-- is_cancelled = true. Прежние строки цикла при закрытии не обновляются.
--
-- Признаки контура существуют только на дневном масштабе. Колонка timeframe
-- нужна, чтобы строка календаря не была подставлена на внутридневной ряд.
--
-- Время в known_at, published_at и остальных точках знания московское, как и в
-- moex_update_time прочих таблиц; пересчёт часового пояса не выполняется.

CREATE TABLE features_cb_rate_meeting_facts (
    id                       uuid          PRIMARY KEY DEFAULT uuidv7(),
    cycle_id                 uuid          NOT NULL,
    fact_type                text          NOT NULL
        CHECK (fact_type IN (
            'meeting_scheduled',
            'decision_published',
            'policy_communication_published')),
    meeting_date             date          NOT NULL,
    scheduled_publication_at timestamp,
    known_at                 timestamp     NOT NULL,
    published_at             timestamp,
    rate_before              numeric(9, 4),
    rate_after               numeric(9, 4),
    effective_from           date,
    is_cancelled             boolean       NOT NULL DEFAULT false
);

CREATE INDEX ix_cb_rate_meeting_facts_cycle
    ON features_cb_rate_meeting_facts (cycle_id);

COMMENT ON TABLE  features_cb_rate_meeting_facts IS
    'Факты вокруг решения по ключевой ставке; одна строка - один факт';
COMMENT ON COLUMN features_cb_rate_meeting_facts.cycle_id IS
    'Идентификатор цикла заседания; передаётся оператором либо выдаётся системой';
COMMENT ON COLUMN features_cb_rate_meeting_facts.known_at IS
    'Момент, с которого факт был известен рынку';
COMMENT ON COLUMN features_cb_rate_meeting_facts.scheduled_publication_at IS
    'Плановый момент публикации; у внепланового заседания может быть пуст';
COMMENT ON COLUMN features_cb_rate_meeting_facts.effective_from IS
    'Дата вступления новой ставки в силу; пусто, если ставка не менялась';
COMMENT ON COLUMN features_cb_rate_meeting_facts.is_cancelled IS
    'Признак строки; цикл закрыт, если у него есть хотя бы одна строка с true';

CREATE TABLE features_cb_rate_calendar (
    d                date          PRIMARY KEY,
    timeframe        text          NOT NULL DEFAULT '1d'
        CHECK (timeframe = '1d'),
    rate_announced   numeric(9, 4),
    rate_effective   numeric(9, 4),
    roisfix_1w       numeric(9, 4),
    roisfix_2w       numeric(9, 4),
    roisfix_1m       numeric(9, 4),
    roisfix_2m       numeric(9, 4),
    roisfix_3m       numeric(9, 4),
    roisfix_6m       numeric(9, 4),
    roisfix_1y       numeric(9, 4),
    roisfix_2y       numeric(9, 4),
    roisfix_known_at timestamp,
    ruonia           numeric(9, 4),
    ruonia_status    text,
    ruonia_known_at  timestamp,
    expert_rate      numeric(9, 4),
    expert_source    text,
    expert_known_at  timestamp
);

COMMENT ON TABLE  features_cb_rate_calendar IS
    'Дневное состояние ключевой ставки и рядов ожиданий; одна строка - один день';
COMMENT ON COLUMN features_cb_rate_calendar.timeframe IS
    'Масштаб ряда; всегда 1d, к внутридневной свече строка не клеится';
COMMENT ON COLUMN features_cb_rate_calendar.rate_announced IS
    'Последняя объявленная ставка; вводится оператором';
COMMENT ON COLUMN features_cb_rate_calendar.rate_effective IS
    'Действующая ставка; вводится оператором';
COMMENT ON COLUMN features_cb_rate_calendar.ruonia_status IS
    'Статус публикации RUONIA в том виде, в каком его передал оператор';
COMMENT ON COLUMN features_cb_rate_calendar.expert_rate IS
    'Экспертное ожидание; отдельный канал, ROISfix не заменяет и с ним не усредняется';
