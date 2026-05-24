-- V005: Опционные серии.
-- PK = series_name — рабочее допущение; уникальность не подтверждена.
-- Если при загрузке обнаружится нарушение уникальности, ключ пересматривается.
-- Источник: GetFuturesSecuritiesAll → CalendarOptionsSeriesDTO (второй проход).
-- Документ: MOEX_Management_Model_v0_2, раздел 5.5.

CREATE TABLE moex_options_series (
    series_name     text        PRIMARY KEY,
    asset_type_name text,
    asset_code      text        NOT NULL,
    series_type     text,
    exec_type       text,
    margin_style    text,
    contract_name   text,
    expiration_date date,
    expiration_type text,
    expiration_time time,
    weekend_session int,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE  moex_options_series IS 'Опционные серии FORTS';
COMMENT ON COLUMN moex_options_series.series_name IS 'Имя серии (PK, рабочее допущение)';
COMMENT ON COLUMN moex_options_series.asset_code IS 'Базовый актив: Si, BR';
