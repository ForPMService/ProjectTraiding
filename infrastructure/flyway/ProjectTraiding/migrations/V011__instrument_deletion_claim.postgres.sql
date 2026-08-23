-- Удаление данных инструмента исполняет владелец данных фоновым исполнителем контура Moex.
-- Захват отмечается моментом времени, а не новым статусом: статус 'started' означает
-- «активная заявка» в семи запросах обоих контуров, и его набор значений остаётся прежним.
ALTER TABLE moex_instrument_data_deletions
    ADD COLUMN claimed_at      timestamptz NULL,
    ADD COLUMN error_message   text        NULL,
    ADD COLUMN next_attempt_at timestamptz NOT NULL DEFAULT now();

-- Очередь исполнителя: незахваченные заявки, у которых подошёл срок следующей попытки.
CREATE INDEX idx_moex_instrument_data_deletions_queue
    ON moex_instrument_data_deletions (next_attempt_at, created_at)
    WHERE status = 'started' AND claimed_at IS NULL;

COMMENT ON COLUMN moex_instrument_data_deletions.claimed_at IS
    'Момент захвата заявки фоновым исполнителем. NULL — заявка ждёт исполнения. При старте исполнителя захваты сбрасываются: незавершённая очистка повторяется целиком, повтор безопасен на каждой таблице';
COMMENT ON COLUMN moex_instrument_data_deletions.error_message IS
    'Текст последнего отказа исполнения. Заполняется при отказе вместе со снятием захвата, очищается при захвате';
COMMENT ON COLUMN moex_instrument_data_deletions.next_attempt_at IS
    'Срок следующей попытки. Отказ сдвигает его на интервал опроса, иначе заблокированная заявка при одной дорожке исполнителя занимала бы очередь бесконечно и не пропускала следующие';
