-- V026__rename_custom_features_sequences.postgres.sql

ALTER SEQUENCE moex_broker_tariffs_id_seq
    RENAME TO custom_features_broker_tariffs_id_seq;

ALTER SEQUENCE moex_instrument_relations_id_seq
    RENAME TO custom_features_instrument_relations_id_seq;