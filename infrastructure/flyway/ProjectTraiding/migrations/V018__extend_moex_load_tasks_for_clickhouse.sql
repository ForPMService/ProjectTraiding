-- V018: Расширение журнала загрузки под запись в ClickHouse.
-- Добавляет версии смысла загрузки, счётчик попыток и аудиторный токен дедупликации
--   в moex_load_tasks и moex_loaded_ranges. Ужесточает storage_target до трёх значений.
-- Производные (имя таблицы ClickHouse, ключ диапазона, токен) НЕ хранятся столбцами —
--   выводятся в коде; здесь только версии и аудиторный след фактически отправленного токена.
-- Старую составную уникальность moex_loaded_ranges НЕ трогаем — она уже задаёт ключ диапазона.
-- База пустая — добавление CHECK безопасно.
-- Документ: Хэндофф_слой_записи_ClickHouse v1.3, раздел 6.1.

-- ═══════════════════════════════════════════════════════════
-- 1. moex_load_tasks: версии, попытки, аудиторный токен
-- ═══════════════════════════════════════════════════════════

ALTER TABLE moex_load_tasks
    ADD COLUMN source_contract_version        text NOT NULL DEFAULT 'moex_history_v1',
    ADD COLUMN writer_version                 text NOT NULL DEFAULT 'clickhouse_writer_v1',
    ADD COLUMN attempt_count                  int  NOT NULL DEFAULT 0,
    ADD COLUMN last_insert_deduplication_token text;

ALTER TABLE moex_load_tasks
    ADD CONSTRAINT chk_moex_load_tasks_storage_target
        CHECK (storage_target IN ('none', 'file', 'clickhouse'));

COMMENT ON COLUMN moex_load_tasks.source_contract_version IS 'Версия смысла загрузки: схема/зона/нормализация/версия парсера. Входит в токен дедупликации';
COMMENT ON COLUMN moex_load_tasks.writer_version IS 'Версия писателя ClickHouse. Входит в токен дедупликации';
COMMENT ON COLUMN moex_load_tasks.attempt_count IS 'Число попыток загрузки этой единицы';
COMMENT ON COLUMN moex_load_tasks.last_insert_deduplication_token IS 'Аудит: фактически отправленный в ClickHouse токен. НЕ источник истины, в уникальности не участвует';

-- ═══════════════════════════════════════════════════════════
-- 2. moex_loaded_ranges: версии, аудиторный токен
-- ═══════════════════════════════════════════════════════════

ALTER TABLE moex_loaded_ranges
    ADD COLUMN source_contract_version        text NOT NULL DEFAULT 'moex_history_v1',
    ADD COLUMN writer_version                 text NOT NULL DEFAULT 'clickhouse_writer_v1',
    ADD COLUMN last_insert_deduplication_token text;

ALTER TABLE moex_loaded_ranges
    ADD CONSTRAINT chk_moex_loaded_ranges_storage_target
        CHECK (storage_target IN ('none', 'file', 'clickhouse'));

COMMENT ON COLUMN moex_loaded_ranges.source_contract_version IS 'Версия смысла загрузки. Входит в токен дедупликации';
COMMENT ON COLUMN moex_loaded_ranges.writer_version IS 'Версия писателя ClickHouse. Входит в токен дедупликации';
COMMENT ON COLUMN moex_loaded_ranges.last_insert_deduplication_token IS 'Аудит: фактически отправленный токен. НЕ источник истины';
