-- V023: Учёт покрытия приёма реального времени в moex_loaded_ranges.
--
-- Задача. Высокая отметка moex_stream_cursors.last_source_time (V019) движется только вперёд
--   и по построению не способна выразить разрыв: приёмник встал в 12:00, поднялся в 14:00,
--   отметка стала 14:00 — промежуток 12:00–14:00 исчезает из виду. Отметка остаётся
--   средством идемпотентности вставки; журналом покрытия она не является и не станет.
--
-- Решение. Приём ведёт покрытие в той же таблице, что и история: один сеанс приёма ряда =
--   одна строка. Разрыв закрывает строку, возобновление открывает новую. Дыра — это
--   промежуток между time_till закрытой строки и time_from следующей, и она лежит в базе.
--
-- Множественность. За один торговый день по одному инструменту строк может быть много:
--   сколько сеансов приёма, столько строк.
--
-- Точность. У исторических строк time_from и time_till равны NULL: их точность суточная,
--   границы несёт пара date_from / date_till. У строк приёма заполнены обе пары.
--   Признак «строка приёма» производный: time_from IS NOT NULL. Отдельной колонкой
--   не хранится — правило V018: производные не хранятся столбцами, выводятся в коде.
--
-- Отступление от V018. Там сказано: «Старую составную уникальность moex_loaded_ranges
--   НЕ трогаем — она уже задаёт ключ диапазона». Это было верно, пока в таблице жил один род
--   строк. С появлением строк приёма прежний ключ становится ложным: два сеанса одного ряда
--   в один день имеют одинаковые date_from и date_till и столкнулись бы друг с другом —
--   то есть множественность была бы запрещена самой схемой. Ключ пересоздаётся.
--
-- Что нельзя закрыть задним числом. Свечи догружаются исторической загрузкой (FillMissing).
--   Сделки и стакан отдаются только за текущий торговый день — утраченный промежуток
--   не восстановим ничем. Для них эта таблица не средство восстановления, а средство
--   честности: она не даст посчитать признаки по промежутку, где данных не было.

-- ═══════════════════════════════════════════════════════════
-- 1. Временные границы сеанса приёма
-- ═══════════════════════════════════════════════════════════

ALTER TABLE moex_loaded_ranges
    ADD COLUMN time_from timestamptz,
    ADD COLUMN time_till timestamptz;

COMMENT ON COLUMN moex_loaded_ranges.time_from IS 'Начало сеанса приёма (рыночное время). NULL у исторических строк — их точность суточная';
COMMENT ON COLUMN moex_loaded_ranges.time_till IS 'Конец сеанса приёма. Двигается сердцебиением, замирает при разрыве. NULL у исторических строк';

-- ═══════════════════════════════════════════════════════════
-- 2. Согласованность границ
-- ═══════════════════════════════════════════════════════════

-- Времена идут парой: либо обоих нет (история), либо есть оба (приём).
-- Одно без другого — строка без смысла.
ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT chk_moex_loaded_ranges_time_pair
        CHECK (
            (time_from IS NULL AND time_till IS NULL)
            OR
            (time_from IS NOT NULL AND time_till IS NOT NULL)
        );

-- Границы не вывернуты. При NULL проверка даёт NULL и строку пропускает — так и надо.
ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT chk_moex_loaded_ranges_time_order
        CHECK (time_till >= time_from);

ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT chk_moex_loaded_ranges_date_order
        CHECK (date_till >= date_from);

-- ═══════════════════════════════════════════════════════════
-- 3. Статусы: добавляются open, closed, crashed
-- ═══════════════════════════════════════════════════════════
--
-- open    — сеанс приёма идёт прямо сейчас; time_till двигается сердцебиением.
-- closed  — сеанс завершён штатно: остановка службы, конец торгов, разрыв соединения.
-- crashed — строка осталась открытой после падения процесса; закрыта принудительно при
--           следующем старте. Граница time_till достоверна с точностью до одного периода
--           сердцебиения — неопределённость измерима и ограничена.
--
-- Прежние ok / partial / stale сохраняются: они принадлежат истории.
-- Имя ограничения — автоген PostgreSQL для inline-CHECK из V015; при ином имени поправить.
-- Приём тот же, что в V020 для moex_load_tasks.

ALTER TABLE moex_loaded_ranges
    DROP CONSTRAINT IF EXISTS moex_loaded_ranges_status_check;

ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT moex_loaded_ranges_status_check
        CHECK (status IN ('ok', 'partial', 'stale', 'open', 'closed', 'crashed'));

-- Статусы приёма невозможны у строки без временных границ.
ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT chk_moex_loaded_ranges_stream_status
        CHECK (
            status NOT IN ('open', 'closed', 'crashed')
            OR time_from IS NOT NULL
        );

COMMENT ON COLUMN moex_loaded_ranges.status IS 'История: ok, partial, stale. Приём: open, closed, crashed';

-- ═══════════════════════════════════════════════════════════
-- 4. Уникальность: множественность сеансов в пределах дня
-- ═══════════════════════════════════════════════════════════
--
-- Новый ключ включает временные границы. У истории они NULL, и NULLS NOT DISTINCT сохраняет
-- прежнее поведение: историческая единица «инструмент-ряд-диапазон» остаётся единственной,
-- повторная загрузка того же диапазона не плодит строк.
--
-- Старое ограничение создано в V015 без имени. Автоген для многоколоночного UNIQUE усечён
-- до 63 знаков и точному предсказанию не поддаётся — в отличие от имени CHECK выше. Поэтому
-- здесь имя не пишется руками, а берётся из системного каталога.

DO $$
DECLARE
    unique_name text;
BEGIN
    SELECT conname INTO unique_name
    FROM pg_constraint
    WHERE conrelid = 'moex_loaded_ranges'::regclass
      AND contype = 'u';

    IF unique_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE moex_loaded_ranges DROP CONSTRAINT %I', unique_name);
    END IF;
END $$;

ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT uq_moex_loaded_ranges_span
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
        );

-- ═══════════════════════════════════════════════════════════
-- 5. Индексы под два рабочих запроса приёма
-- ═══════════════════════════════════════════════════════════

-- Запрос при старте процесса: какие строки остались открытыми — их надо закрыть статусом
-- crashed. Частичный индекс: открытых строк единицы, полный проход по таблице не нужен.
CREATE INDEX idx_moex_loaded_ranges_open
    ON moex_loaded_ranges (secid, data_kind)
    WHERE status = 'open';

-- Запрос расчёта дыр: строки приёма по инструменту и ряду, упорядоченные по времени.
-- Непокрытые промежутки берутся как разрывы между соседними строками — с последующим
-- пересечением с торговым расписанием, иначе каждая ночь будет числиться дырой.
CREATE INDEX idx_moex_loaded_ranges_stream_span
    ON moex_loaded_ranges (secid, data_kind, candle_interval, time_from)
    WHERE time_from IS NOT NULL;
