-- Журнал этапов дивидендного процесса.
--
-- Одна строка = один факт, ставший известным рынку. Строки не переписываются при
-- появлении следующего факта: точка знания задаётся known_at и должна оставаться
-- неизменной. Отрицательное решение и отмена — тоже факты со своей точкой знания,
-- поэтому вносятся новой строкой с is_cancelled = true, а не правкой прежней.
--
-- Время в known_at московское, как и в moex_update_time остальных таблиц;
-- пересчёт часового пояса не выполняется. Сам этап хранится датой: источник
-- публикует время далеко не всегда, а придумывать его нельзя.

CREATE TABLE moex_dividend_events (
    id                       uuid        PRIMARY KEY DEFAULT uuidv7(),
    secid                    text        NOT NULL,
    event_type               text        NOT NULL
        CHECK (event_type IN (
            'board_meeting_scheduled',
            'board_recommendation',
            'shareholder_meeting_scheduled',
            'shareholder_approved',
            'record_date',
            'payment')),
    event_date               date        NOT NULL,
    known_at                 timestamp   NOT NULL,
    is_cancelled             boolean     NOT NULL DEFAULT false,
    dividend_amount          numeric(18, 6),
    currency                 text,
    record_date              date,
    last_eligible_trade_date date,
    payment_date             date,
    source_note              text,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_moex_dividend_events_currency
        CHECK (dividend_amount IS NULL OR currency IS NOT NULL)
);

CREATE INDEX ix_dividend_events_secid_known
    ON moex_dividend_events (secid, known_at);

COMMENT ON TABLE  moex_dividend_events IS
    'Этапы дивидендного процесса; одна строка - один ставший известным факт';
COMMENT ON COLUMN moex_dividend_events.event_date IS
    'Дата самого этапа';
COMMENT ON COLUMN moex_dividend_events.known_at IS
    'Момент, с которого факт был известен рынку; признак видит строку только при known_at <= момент расчёта';
COMMENT ON COLUMN moex_dividend_events.is_cancelled IS
    'Решение отрицательное или отменяющее; вносится новой строкой, прежние строки не правятся';
COMMENT ON COLUMN moex_dividend_events.dividend_amount IS
    'Известная на этом этапе сумма на акцию; NULL, если ещё не объявлена';
COMMENT ON COLUMN moex_dividend_events.last_eligible_trade_date IS
    'Последний торговый день покупки с правом на дивиденд; вносится вручную';
