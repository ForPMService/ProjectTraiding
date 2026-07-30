-- Код серии срочного контракта.
-- Используется Московской биржей как ключ запроса открытого интереса.
-- Не совпадает с кодом базового актива для всех серий:
-- у SRU6 sectype = 'SR', asset_code = 'SBRF'.

ALTER TABLE moex_futures_details
    ADD COLUMN sectype text;

COMMENT ON COLUMN moex_futures_details.sectype IS
    'Код серии срочного контракта. MOEX: SECTYPE. Ключ запроса FUTOI';
