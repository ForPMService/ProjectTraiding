-- Серия срочных контрактов как сущность справочника.
-- Источник публикует открытый интерес по серии, а не по контракту: запрос по SRU6
-- возвращает пустой набор, запрос по SR — данные. Строка серии нужна именно в
-- moex_instruments: на эту таблицу ссылаются внешними ключами и moex_load_tasks.secid,
-- и moex_loaded_ranges.secid.
--
-- Серия не является торгуемым инструментом: она исключена из каталога витрины,
-- операторских счётчиков и приёмника реального времени.

ALTER TABLE moex_instruments
    DROP CONSTRAINT moex_instruments_instrument_type_check;

ALTER TABLE moex_instruments
    ADD CONSTRAINT moex_instruments_instrument_type_check
    CHECK (instrument_type IN ('stock', 'futures', 'futures_series'));

COMMENT ON COLUMN moex_instruments.instrument_type IS
    'stock, futures или futures_series — серия срочных контрактов, субъект открытого интереса, не торгуемый инструмент';
