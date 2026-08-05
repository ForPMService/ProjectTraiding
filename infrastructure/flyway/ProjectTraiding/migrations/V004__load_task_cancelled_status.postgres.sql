-- Статус cancelled: задание снято оператором, пока оно не находилось в running.
-- Команда отмены переводит в cancelled два состояния, и смысл у них разный:
-- для pending это отказ до запуска, для partial — отказ от дальнейшего или
-- повторного выполнения уже начинавшегося задания.
-- Отмена не сбой и не успех, это отдельный исход, и статус должен говорить правду:
-- сама операция отмены не проходит через координатор загрузки и не формирует его
-- итоговую метрику выполнения. Индивидуальный итог задания сохраняется здесь,
-- статусом в базе; агрегат команды — журнальным событием контура Management.
--
-- Индексы не меняются намеренно. И частичный индекс подбора idx_moex_load_tasks_pickup,
-- и уникальный индекс активных заданий idx_moex_load_tasks_active_logical_unique
-- перечисляют статусы явно, cancelled в них не входит. Поэтому снятое задание
-- выпадает из уникальности активных и тот же диапазон можно поставить заново.
--
-- Возобновление снятого задания по прежнему идентификатору невозможно и это
-- сознательное решение: MoexLoadTaskWriter.MarkRunningAsync принимает только
-- pending, error и partial. Для повторной загрузки создаётся новое задание
-- одиночной или массовой постановкой.

ALTER TABLE moex_load_tasks
    DROP CONSTRAINT moex_load_tasks_status_check;

ALTER TABLE moex_load_tasks
    ADD CONSTRAINT moex_load_tasks_status_check
        CHECK (status IN ('pending', 'running', 'done', 'error', 'partial', 'cancelled'));

COMMENT ON COLUMN moex_load_tasks.status IS
    'pending, running, done, error, partial, cancelled. cancelled — снято оператором командой Management, пока задание не находилось в running: для pending это отказ до запуска, для partial — отказ от дальнейшего выполнения. По прежнему идентификатору не возобновляется, для повторной загрузки создаётся новое задание';

COMMENT ON COLUMN moex_load_tasks.stop_reason IS
    'empty_cursor, range_exhausted, safety_cap_hit, operator_cancelled';
