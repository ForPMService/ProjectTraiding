-- Оповещения (MegaAlerts), акции. Источник: MegaAlertsAssetSchema / MegaAlertsAssetsDTO.
-- Событийный ряд, история с 2024 года. Ключевые столбцы: secid, source_time, alert_type.

CREATE TABLE moex_megaalerts_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
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
