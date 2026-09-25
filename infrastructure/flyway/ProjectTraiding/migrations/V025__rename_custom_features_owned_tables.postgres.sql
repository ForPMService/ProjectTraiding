-- Tables owned by ProjectTraiding.CustomFeatures retain their data and schema;
-- only their physical names are aligned with the owning module.

ALTER TABLE moex_calendar_days
    RENAME TO custom_features_calendar_days;

ALTER TABLE moex_trading_periods
    RENAME TO custom_features_trading_periods;

ALTER TABLE moex_instrument_board_intervals
    RENAME TO custom_features_instrument_board_intervals;

ALTER TABLE moex_futures_expirations
    RENAME TO custom_features_futures_expirations;

ALTER TABLE moex_splits
    RENAME TO custom_features_splits;

ALTER TABLE moex_dividend_events
    RENAME TO custom_features_dividend_events;

ALTER TABLE moex_instrument_relations
    RENAME TO custom_features_instrument_relations;

ALTER TABLE moex_broker_tariffs
    RENAME TO custom_features_broker_tariffs;

ALTER TABLE features_cb_rate_calendar
    RENAME TO custom_features_cb_rate_calendar;

ALTER TABLE features_cb_rate_meeting_facts
    RENAME TO custom_features_cb_rate_meeting_facts;

-- PostgreSQL keeps explicit and generated constraint names when a table is
-- renamed. Rename every affected constraint, including its supporting index.
DO $$
DECLARE
    constraint_rename record;
BEGIN
    FOR constraint_rename IN
        WITH renamed_tables (new_name) AS (
            VALUES
                ('custom_features_calendar_days'),
                ('custom_features_trading_periods'),
                ('custom_features_instrument_board_intervals'),
                ('custom_features_futures_expirations'),
                ('custom_features_splits'),
                ('custom_features_dividend_events'),
                ('custom_features_instrument_relations'),
                ('custom_features_broker_tariffs'),
                ('custom_features_cb_rate_calendar'),
                ('custom_features_cb_rate_meeting_facts')
        )
        SELECT
            constraint_catalog.conrelid::regclass AS table_name,
            constraint_catalog.conname AS old_constraint_name,
            left(
                CASE
                    WHEN constraint_catalog.conname LIKE 'features_%'
                        THEN 'custom_' || constraint_catalog.conname
                    ELSE replace(constraint_catalog.conname, 'moex_', 'custom_features_')
                END,
                63) AS new_constraint_name
        FROM pg_constraint AS constraint_catalog
        INNER JOIN renamed_tables
            ON constraint_catalog.conrelid = to_regclass(
                format('%I.%I', current_schema(), renamed_tables.new_name))
        WHERE position('moex_' IN constraint_catalog.conname) > 0
           OR constraint_catalog.conname LIKE 'features_%'
    LOOP
        EXECUTE format(
            'ALTER TABLE %s RENAME CONSTRAINT %I TO %I',
            constraint_rename.table_name,
            constraint_rename.old_constraint_name,
            constraint_rename.new_constraint_name);
    END LOOP;
END $$;

-- Rename standalone indexes. Constraint-owned indexes were renamed above with
-- their constraints.
DO $$
DECLARE
    index_rename record;
BEGIN
    FOR index_rename IN
        WITH renamed_tables (new_name) AS (
            VALUES
                ('custom_features_calendar_days'),
                ('custom_features_trading_periods'),
                ('custom_features_instrument_board_intervals'),
                ('custom_features_futures_expirations'),
                ('custom_features_splits'),
                ('custom_features_dividend_events'),
                ('custom_features_instrument_relations'),
                ('custom_features_broker_tariffs'),
                ('custom_features_cb_rate_calendar'),
                ('custom_features_cb_rate_meeting_facts')
        )
        SELECT
            index_catalog.oid::regclass AS index_name,
            index_catalog.relname AS old_index_name,
            left(
                CASE
                    WHEN index_catalog.relname LIKE 'features_%'
                        THEN 'custom_' || index_catalog.relname
                    ELSE replace(index_catalog.relname, 'moex_', 'custom_features_')
                END,
                63) AS new_index_name
        FROM pg_index
        INNER JOIN pg_class AS index_catalog ON index_catalog.oid = pg_index.indexrelid
        INNER JOIN renamed_tables
            ON pg_index.indrelid = to_regclass(
                format('%I.%I', current_schema(), renamed_tables.new_name))
        LEFT JOIN pg_constraint AS constraint_catalog
            ON constraint_catalog.conindid = pg_index.indexrelid
        WHERE constraint_catalog.oid IS NULL
          AND (
              position('moex_' IN index_catalog.relname) > 0
              OR index_catalog.relname LIKE 'features_%')
    LOOP
        EXECUTE format(
            'ALTER INDEX %s RENAME TO %I',
            index_rename.index_name,
            index_rename.new_index_name);
    END LOOP;
END $$;
