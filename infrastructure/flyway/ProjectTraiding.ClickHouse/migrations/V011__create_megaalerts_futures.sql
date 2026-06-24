-- Оповещения (MegaAlerts), фьючерсы. Источник: MegaAlertsFuturesSchema / MegaAlertsFuturesDTO.
-- Ключевые столбцы: secid, source_time, alert_type. asset_code неключевой → Nullable.

CREATE TABLE moex_megaalerts_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    alert_type   LowCardinality(String),
    threshold    Nullable(Float64),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow'))
)
ENGINE = MergeTree
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, alert_type)
SETTINGS non_replicated_deduplication_window = 1000;
