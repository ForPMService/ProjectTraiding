-- Задания удаления рабочих данных инструмента.
--
-- Таблица выполняет две работы одновременно и потому не требует ни отдельного
-- признака в карточке инструмента, ни отдельной системы блокировок:
--   история  — какие удаления запускались и когда;
--   признак  — строка со статусом 'started' означает, что инструмент сейчас
--              очищается либо очистка прервалась незавершённой, и запускать
--              по нему новую запись данных нельзя.
--
-- Одновременность двух заданий по одному инструменту запрещена частичным
-- уникальным индексом, а не проверкой в коде: проверка перед вставкой оставляет
-- окно гонки между SELECT и INSERT, индекс окна не оставляет.
--
-- ВНИМАНИЕ. Индекс запрещает две СТРОКИ, но не два одновременно работающих
-- процесса очистки, и не останавливает команду записи, которая началась до
-- фиксации строки. Обе границы разобраны в разделе 5 задания, обе
-- эксплуатационные.
--
-- Строки не удаляются никогда, кроме одного случая: задание, поставленное и
-- тут же снятое проверками, которые ничего не успели тронуть. Всё остальное
-- остаётся историей, поэтому продолжение прерванного удаления работает с той
-- же строкой, а не заводит новую.
--
-- Момента завершения таблица не хранит намеренно: заданием задачи он не
-- затребован, а created_at вместе со статусом отвечают на оба вопроса,
-- ради которых таблица заведена.

CREATE TABLE moex_instrument_data_deletions (
    id         uuid        PRIMARY KEY DEFAULT uuidv7(),
    secid      text        NOT NULL
                           REFERENCES moex_instruments (secid),
    created_at timestamptz NOT NULL DEFAULT now(),
    status     text        NOT NULL DEFAULT 'started',

    CONSTRAINT moex_instrument_data_deletions_status_check
        CHECK (status IN ('started', 'finished'))
);

-- Блокировка второго задания по тому же инструменту.
CREATE UNIQUE INDEX idx_moex_instrument_data_deletions_active
    ON moex_instrument_data_deletions (secid)
    WHERE status = 'started';

-- История удалений по инструменту, свежие сверху.
CREATE INDEX idx_moex_instrument_data_deletions_history
    ON moex_instrument_data_deletions (secid, created_at DESC);

COMMENT ON TABLE  moex_instrument_data_deletions IS 'Задания удаления рабочих данных инструмента: история запусков и признак идущей очистки';
COMMENT ON COLUMN moex_instrument_data_deletions.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_instrument_data_deletions.created_at IS 'Момент постановки задания удаления. При продолжении прерванного удаления не обновляется: это то же задание';
COMMENT ON COLUMN moex_instrument_data_deletions.status IS 'started — очистка идёт либо прервалась незавершённой, новая запись данных этого инструмента запрещена; finished — очистка завершена, обычная работа разрешена';
