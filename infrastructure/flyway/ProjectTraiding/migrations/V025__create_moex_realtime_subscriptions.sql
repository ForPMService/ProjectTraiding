-- V025: Список наблюдения приёмника реального времени.
--
-- Задача. Приёмник (сделки, стакан) читал весь moex_instruments и опрашивал каждую строку —
-- сотни инструментов, включая дальние неликвидные фьючерсы. Справочник отвечает на вопрос
-- «какие инструменты известны», а приёмнику нужен ответ на «какие сейчас разрешено собирать».
-- Это разные вопросы; смешивать их — ошибка. Здесь заводится явный список подписок.
--
-- Зерно строки — пара инструмент + вид данных: сделки и стакан включаются независимо
-- (инструмент без стакана можно слушать только по сделкам). Рынок здесь НЕ хранится: он
-- свойство инструмента и берётся из moex_instruments соединением — дублировать его столбцом
-- значило бы хранить производное.
--
-- На старте или после перезапуска пустой список означает пустой приём: если строк нет,
-- приёмник не опрашивает ничего.

CREATE TABLE moex_realtime_subscriptions (
    secid       text        NOT NULL
                            REFERENCES moex_instruments (secid),
    data_kind   text        NOT NULL CHECK (data_kind IN ('trades', 'orderbook')),
    enabled     boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (secid, data_kind)
);

COMMENT ON TABLE  moex_realtime_subscriptions IS 'Список наблюдения приёмника: какие инструменты и виды данных собирать';
COMMENT ON COLUMN moex_realtime_subscriptions.secid IS 'Инструмент, FK → moex_instruments';
COMMENT ON COLUMN moex_realtime_subscriptions.data_kind IS 'trades или orderbook — включаются независимо';
COMMENT ON COLUMN moex_realtime_subscriptions.enabled IS 'false — временно снять с наблюдения, не удаляя строку';
COMMENT ON COLUMN moex_realtime_subscriptions.updated_at IS 'Обновляется при каждом изменении строки; будущие команды Management обязаны ставить updated_at = now() при UPDATE';
