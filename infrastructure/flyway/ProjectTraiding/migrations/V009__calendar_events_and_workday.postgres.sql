-- Корпоративные события и признак рабочего дня по движку.
--
-- Событие описывается двумя полями: тип отвечает на вопрос «что произошло»,
-- стадия — «на каком этапе». Рекомендация совета директоров и утверждение
-- собранием различаются стадией, а не типом: сумма и бумага у них те же,
-- меняется только степень определённости. Рынок реагирует на обе.

-- 1. Полный набор типов событий.
--
-- Перенос значений не выполняется намеренно: прежнее ограничение допускало
-- ровно четыре значения, и все четыре входят в новый набор. Строка с иным
-- значением означала бы, что ограничение обходили, — такую миграция обязана
-- остановить, а не молча исправить.

ALTER TABLE moex_manual_events
    DROP CONSTRAINT IF EXISTS moex_manual_events_event_type_check;

ALTER TABLE moex_manual_events
    ADD CONSTRAINT moex_manual_events_event_type_check
        CHECK (event_type IN (
            'dividend',
            'meeting',
            'issue',
            'buyback',
            'delisting_announced'));

-- 2. Стадия события.
--
-- Значение по умолчанию 'announced' подходит для событий, у которых стадии нет:
-- собрание либо состоялось, либо нет, промежуточного состояния у него не бывает.

ALTER TABLE moex_manual_events
    ADD COLUMN event_stage text NOT NULL DEFAULT 'announced'
        CHECK (event_stage IN ('announced', 'recommended', 'approved', 'completed', 'cancelled'));

COMMENT ON COLUMN moex_manual_events.event_type IS
    'dividend — выплата; meeting — собрание акционеров; issue — дополнительная эмиссия; '
    'buyback — выкуп акций; delisting_announced — объявление о делистинге';
COMMENT ON COLUMN moex_manual_events.event_stage IS
    'recommended — совет директоров рекомендовал; approved — собрание утвердило; '
    'completed — исполнено; cancelled — отменено; announced — стадия неприменима';
COMMENT ON COLUMN moex_manual_events.secid IS
    'Бумага. Внешнего ключа на moex_instruments намеренно нет: справочник содержит '
    'торгуемые сейчас инструменты, а события нужны и по делистингованным бумагам.';

-- Индекс по (secid, event_date) не создаётся: он уже есть с прежней миграции
-- под именем ix_manual_events_secid_date. Второй индекс на тех же колонках
-- занял бы место и замедлил запись, ничего не ускорив.

-- 3. Признак рабочего дня из справочника движка.
--
-- Независимый от календаря источник того же факта: календарь берётся из
-- /calendars/{market}.json, признак — из раздела dailytable ответа движка.
-- Расхождение между ними означает ошибку на одной из сторон и ищется запросом.
-- Пусто там, где записи в справочнике движка нет: dailytable содержит только
-- дни-исключения, обычных дней в нём не бывает.

ALTER TABLE moex_calendar_days
    ADD COLUMN engine_is_work_day int;

COMMENT ON COLUMN moex_calendar_days.engine_is_work_day IS
    'Признак рабочего дня из dailytable движка: 1 или 0. NULL — записи в справочнике движка нет';
