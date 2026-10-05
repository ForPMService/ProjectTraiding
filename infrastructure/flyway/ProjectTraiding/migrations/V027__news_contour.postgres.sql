-- V027: контур внешних новостей и событий.
-- Семь таблиц CustomFeatures.News.
--
-- Все временные значения новостного контура хранятся как московское стенное
-- время в timestamp without time zone. DEFAULT now() для available_at и
-- created_at намеренно не используется.
--
-- Историческая оценка инструмента не имеет внешнего ключа по secid:
-- изменение текущего состава анализа или справочника инструментов не должно
-- ограничивать уже сохранённую историю.

CREATE TABLE custom_features_news_event_types (
    code text NOT NULL,

    CONSTRAINT pk_custom_features_news_event_types
        PRIMARY KEY (code),

    CONSTRAINT ck_custom_features_news_event_types_code_not_blank
        CHECK (code <> ''),

    CONSTRAINT ck_custom_features_news_event_types_code_trimmed
        CHECK (code = btrim(code))
);


CREATE TABLE custom_features_news_global_events (
    id   uuid NOT NULL DEFAULT uuidv7(),
    name text NOT NULL,

    CONSTRAINT pk_custom_features_news_global_events
        PRIMARY KEY (id),

    CONSTRAINT uq_custom_features_news_global_events_name
        UNIQUE (name),

    CONSTRAINT ck_custom_features_news_global_events_name_not_blank
        CHECK (name <> ''),

    CONSTRAINT ck_custom_features_news_global_events_name_trimmed
        CHECK (name = btrim(name))
);


CREATE TABLE custom_features_news_instruments (
    secid text NOT NULL,

    CONSTRAINT pk_custom_features_news_instruments
        PRIMARY KEY (secid),

    CONSTRAINT fk_custom_features_news_instruments_secid
        FOREIGN KEY (secid)
        REFERENCES moex_instruments (secid)
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
);


CREATE TABLE custom_features_news_publications (
    id           uuid      NOT NULL DEFAULT uuidv7(),
    source       text      NOT NULL,
    raw_text     text      NOT NULL,
    published_at timestamp,
    received_at  timestamp,
    known_at     timestamp NOT NULL,

    CONSTRAINT pk_custom_features_news_publications
        PRIMARY KEY (id),

    CONSTRAINT ck_custom_features_news_publications_has_source_time
        CHECK (published_at IS NOT NULL OR received_at IS NOT NULL),

    CONSTRAINT ck_custom_features_news_publications_known_at
        CHECK (known_at = COALESCE(published_at, received_at))
);

CREATE INDEX ix_custom_features_news_publications_known_at
    ON custom_features_news_publications (known_at);


CREATE TABLE custom_features_news_analyses (
    id             uuid      NOT NULL DEFAULT uuidv7(),
    publication_id uuid      NOT NULL,
    agent_version  text      NOT NULL,
    accepted       boolean   NOT NULL,
    available_at   timestamp NOT NULL,

    CONSTRAINT pk_custom_features_news_analyses
        PRIMARY KEY (id),

    CONSTRAINT uq_custom_features_news_analyses_publication_id
        UNIQUE (publication_id),

    CONSTRAINT fk_custom_features_news_analyses_publication_id
        FOREIGN KEY (publication_id)
        REFERENCES custom_features_news_publications (id)
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
);


CREATE TABLE custom_features_news_facts (
    id              uuid      NOT NULL DEFAULT uuidv7(),
    analysis_id     uuid      NOT NULL,
    event_type      text      NOT NULL,
    summary         text      NOT NULL,
    occurred_at     timestamp,
    global_event_id uuid,

    CONSTRAINT pk_custom_features_news_facts
        PRIMARY KEY (id),

    CONSTRAINT fk_custom_features_news_facts_analysis_id
        FOREIGN KEY (analysis_id)
        REFERENCES custom_features_news_analyses (id)
        ON UPDATE NO ACTION
        ON DELETE NO ACTION,

    CONSTRAINT fk_custom_features_news_facts_event_type
        FOREIGN KEY (event_type)
        REFERENCES custom_features_news_event_types (code)
        ON UPDATE NO ACTION
        ON DELETE NO ACTION,

    CONSTRAINT fk_custom_features_news_facts_global_event_id
        FOREIGN KEY (global_event_id)
        REFERENCES custom_features_news_global_events (id)
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
);

CREATE INDEX ix_custom_features_news_facts_analysis_id
    ON custom_features_news_facts (analysis_id);

CREATE INDEX ix_custom_features_news_facts_event_type
    ON custom_features_news_facts (event_type);

CREATE INDEX ix_custom_features_news_facts_global_event_id
    ON custom_features_news_facts (global_event_id);


CREATE TABLE custom_features_news_fact_instrument_assessments (
    fact_id              uuid      NOT NULL,
    secid                text      NOT NULL,
    instrument_relevance numeric   NOT NULL,
    direction            smallint,
    impact_strength      numeric,
    created_at           timestamp NOT NULL,

    CONSTRAINT pk_custom_features_news_fact_instrument_assessments
        PRIMARY KEY (fact_id, secid),

    CONSTRAINT fk_custom_features_news_fact_instrument_assessments_fact_id
        FOREIGN KEY (fact_id)
        REFERENCES custom_features_news_facts (id)
        ON UPDATE NO ACTION
        ON DELETE NO ACTION,

    CONSTRAINT ck_custom_features_news_fact_instrument_assessments_relevance
        CHECK (
            instrument_relevance >= 0
            AND instrument_relevance <= 1
        ),

    CONSTRAINT ck_custom_features_news_fact_instrument_assessments_direction
        CHECK (
            direction IS NULL
            OR direction IN (-1, 0, 1)
        ),

    CONSTRAINT ck_custom_features_news_fact_instrument_assessments_impact
        CHECK (
            impact_strength IS NULL
            OR (
                impact_strength >= 0
                AND impact_strength <= 1
            )
        )
);

CREATE INDEX ix_custom_features_news_fact_instrument_assessments_secid
    ON custom_features_news_fact_instrument_assessments (secid);


COMMENT ON TABLE custom_features_news_event_types IS
    'Управляемый справочник допустимых типов событий новостного контура';

COMMENT ON TABLE custom_features_news_global_events IS
    'Глобальные длительные сюжеты, связывающие атомарные факты';

COMMENT ON TABLE custom_features_news_instruments IS
    'Текущий состав инструментов, относительно которых новостной агент выполняет оценку';

COMMENT ON TABLE custom_features_news_publications IS
    'Исходные публикации, записанные только после валидного результата агента';

COMMENT ON COLUMN custom_features_news_publications.published_at IS
    'Достоверное время публикации источника; московское стенное время';

COMMENT ON COLUMN custom_features_news_publications.received_at IS
    'Время получения записи адаптером; московское стенное время; не подменяется временем Writer';

COMMENT ON COLUMN custom_features_news_publications.known_at IS
    'Точка знания исходной публикации: published_at, если оно достоверно, иначе received_at';

COMMENT ON TABLE custom_features_news_analyses IS
    'Результаты агентного разбора; ровно один analysis на publication';

COMMENT ON COLUMN custom_features_news_analyses.available_at IS
    'Операционный момент доступности валидного результата анализа; московское стенное время';

COMMENT ON TABLE custom_features_news_facts IS
    'Атомарные факты, извлечённые из публикации агентом';

COMMENT ON TABLE custom_features_news_fact_instrument_assessments IS
    'Исторические оценки связи атомарного факта с инструментом';

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.secid IS
    'Инструмент на момент анализа; внешний ключ намеренно отсутствует для сохранения истории';

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.instrument_relevance IS
    'Оценка экономической связи факта с инструментом в диапазоне 0..1';

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.direction IS
    '-1 отрицательное, 0 нейтральное, 1 положительное воздействие, NULL если направление не определено';

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.impact_strength IS
    'Предполагаемая сила воздействия в диапазоне 0..1; NULL если оценка не определена';

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.created_at IS
    'Операционный момент записи оценки; в текущей модели равен analysis.available_at';
