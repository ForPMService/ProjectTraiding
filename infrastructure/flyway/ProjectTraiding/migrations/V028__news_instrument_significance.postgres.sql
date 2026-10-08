
-- V028__news_instrument_significance.postgres.sql

ALTER TABLE custom_features_news_fact_instrument_assessments
    RENAME COLUMN impact_strength TO instrument_significance;

ALTER TABLE custom_features_news_fact_instrument_assessments
    RENAME CONSTRAINT
        ck_custom_features_news_fact_instrument_assessments_impact
    TO ck_custom_features_news_assessments_significance;

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.instrument_significance IS
    'Экономическая существенность события для инструмента; 0..1 или NULL. Не прогноз размера движения цены или рыночной реакции';

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.direction IS
    'Экономический вектор события: -1 отрицательный, 0 нейтральный, 1 положительный, NULL неопределённый. Не прогноз цены';

COMMENT ON COLUMN custom_features_news_analyses.available_at IS
    'Момент доступности анализа. Realtime: точка знания оценок. Historical: техническое время исторической загрузки';

COMMENT ON COLUMN custom_features_news_fact_instrument_assessments.created_at IS
    'Время записи оценки, равное analysis.available_at в одноэтапной модели';

COMMENT ON TABLE custom_features_news_fact_instrument_assessments IS
    'Оценки экономической связи фактов с инструментами для Historical и Realtime';
