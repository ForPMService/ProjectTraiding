-- ═══════════════════════════════════════════════════════════════════════════
-- V001: Начальная схема ProjectTraiding (ClickHouse).
--
-- Свёртка прежней цепочки V001–V016 в одно итоговое состояние. Прежние файлы
-- удалены.
--
-- Существовавшие данные приёма реального времени (свечи, лента сделок, стакан)
-- удалены намеренно. Перенос в новую начальную схему не выполняется: свечи
-- добирает историческая загрузка, лента сделок и стакан за прошедшие дни
-- источником не отдаются и восстановлению не подлежат — это принятая потеря.
--
-- ОТСТУПЛЕНИЕ ОТ ПРАВИЛА «одна операция на миграцию». Настройки Flyway для
-- ClickHouse требуют держать одну операцию создания таблицы на файл: отката там
-- нет, и правило локализует сбой. Здесь правило нарушено намеренно — база
-- создаётся с нуля и пустой, поэтому цена сбоя равна повторному сносу пустой
-- базы, а не разбору полусозданной схемы с данными. На последующие миграции
-- отступление НЕ распространяется: там правило действует как прежде.
--
-- Что изменено против прежней цепочки (три осознанные правки):
--
--   1. Десять статистических таблиц переведены на ReplacingMergeTree по версии
--      ingested_at. Прежде они были обычным MergeTree, и повторная загрузка того
--      же дня накапливала дубли вместо замены строк. Токен вставки от этого не
--      спасает: у растущего дня каждая следующая пачка имеет другую последнюю
--      метку времени и другое число строк, значит другой токен, и первый уровень
--      дедупликации её пропускает.
--
--      Почему ingested_at, а не systime: systime обнуляем, и его смысл как
--      версии исправления источником нигде не закреплён. Версия схлопывания
--      обязана быть не обнуляемой. Значение по умолчанию означает: побеждает
--      строка, загруженная позже.
--
--      Строгой монотонности здесь нет и обещать её нельзя: now64(3) — показания
--      системных часов, а не возрастающий счётчик. При равных значениях версии
--      исход решает порядок строк при слиянии, а не смысл данных. Для интервала
--      обновлений в минуты и часы миллисекундной точности достаточно.
--
--      Ключи сортировки сохранены без единого изменения.
--
--      ПОРЯДОК ПЕРВОЙ ЗАГРУЗКИ. Натуральность ключей сортировки статистической
--      семьи на данных НЕ проверена. Если ключ окажется неполным, схлопывание
--      молча удалит законные разные строки — это потеря данных, а не только
--      испорченная диагностика. Поэтому до первой загрузки слияния на десяти
--      статистических таблицах останавливаются (SYSTEM STOP MERGES), диапазон
--      грузится ОДИН раз, счётчики строк на ключ проверяются на базовых
--      таблицах, и лишь затем слияния включаются (SYSTEM START MERGES).
--      Проверять после слияний поздно: пустой результат уже ничего не докажет.
--      Особое внимание — оповещениям и открытому интересу.
--
--   2. Свечные таблицы десяти минут, часа и дня получили столбец ingest_priority
--      и тот же схлопывающий движок, что и минутная. Это не улучшение, а
--      исправление: карта столбцов CandlesRowMap одна на все интервалы и
--      безусловно передаёт ingest_priority в списке столбцов вставки. Прежние
--      таблицы этого столбца не имели, и историческая загрузка любого интервала,
--      кроме минутного, отвергалась сервером.
--
--   3. Добавлены представления чтения с суффиксом _final — по одному на каждую
--      схлопывающую таблицу, то есть на все семнадцать.
--
--      До фонового слияния обычный SELECT видит несколько версий строки.
--      Обязанность помнить про модификатор FINAL нельзя оставлять каждому
--      будущему читателю: забытый модификатор даёт молча неверный результат,
--      а не ошибку. Стоимость модификатора представление не отменяет — запросы
--      обязаны ограничиваться инструментом и диапазоном времени.
--
--      Суффикс называет механизм, а не смысл. Слово current намеренно не занято:
--      оно понадобится для семантики текущего дня.
--
-- Единые правила, сохранённые без изменений:
--   время — DateTime64(3, 'Europe/Moscow'), рыночное время названо source_time;
--   ключевые столбцы обязательны, все неключевые — Nullable (режим разведки
--     данных: пустую ячейку источника храним как есть, разбираем потом);
--   окно дедупликации вставок non_replicated_deduplication_window = 1000;
--   тип столбца повторяет тип поля объекта передачи данных (требование
--     двоичной вставки).
-- ═══════════════════════════════════════════════════════════════════════════


-- ═══════════════════════════════════════════════════════════
-- РАЗДЕЛ 1. Свечи. Четыре интервала MOEX: 1, 10, 60, 24.
--
-- У минутной свечи два писателя — историческая загрузка и приёмник реального
-- времени. Версия схлопывания ingest_priority решает столкновение: история
-- пишет 1, приёмник 0, при равном ключе (secid, begin) побеждает большая.
-- У старших интервалов писатель один (история), но форма единая: карта
-- столбцов одна на все интервалы.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_candles_1m
(
    secid            LowCardinality(String),
    open             Nullable(Float64),
    close            Nullable(Float64),
    high             Nullable(Float64),
    low              Nullable(Float64),
    value            Nullable(Float64),
    volume           Nullable(Float64),
    begin            DateTime64(3, 'Europe/Moscow'),
    end              Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority  UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 1000;


CREATE TABLE moex_candles_10m
(
    secid            LowCardinality(String),
    open             Nullable(Float64),
    close            Nullable(Float64),
    high             Nullable(Float64),
    low              Nullable(Float64),
    value            Nullable(Float64),
    volume           Nullable(Float64),
    begin            DateTime64(3, 'Europe/Moscow'),
    end              Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority  UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 1000;


CREATE TABLE moex_candles_1h
(
    secid            LowCardinality(String),
    open             Nullable(Float64),
    close            Nullable(Float64),
    high             Nullable(Float64),
    low              Nullable(Float64),
    value            Nullable(Float64),
    volume           Nullable(Float64),
    begin            DateTime64(3, 'Europe/Moscow'),
    end              Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority  UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 1000;


CREATE TABLE moex_candles_1d
(
    secid            LowCardinality(String),
    open             Nullable(Float64),
    close            Nullable(Float64),
    high             Nullable(Float64),
    low              Nullable(Float64),
    value            Nullable(Float64),
    volume           Nullable(Float64),
    begin            DateTime64(3, 'Europe/Moscow'),
    end              Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority  UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYear(begin)
ORDER BY (secid, begin)
SETTINGS non_replicated_deduplication_window = 1000;


-- ═══════════════════════════════════════════════════════════
-- РАЗДЕЛ 2. Приём реального времени: лента сделок и стакан.
--
-- Ленты сделок акций и фьючерсов различаются составом полей — общей таблицы
-- быть не может. Стакан один на оба рынка: структура совпадает, рынки различает
-- board_id.
--
-- У стакана source_time собирается из SEQNUM: в ответе даты нет, UPDATETIME даёт
-- лишь время суток. seq_num и update_time столбцами НЕ хранятся — оба выводятся
-- из source_time без потерь. trade_session_date — торговый день по блоку
-- dataversion; на выходных торгах он расходится с моментом снимка.
--
-- Разбиение стакана по дню и срок жизни тридцать суток: стакан невосстановим,
-- но объёмен — при опросе раз в пять секунд двадцать инструментов дают около
-- пяти миллионов строк в день. Истёкшая часть отбрасывается целиком.
-- ═══════════════════════════════════════════════════════════

CREATE TABLE moex_trades_stock
(
    secid               LowCardinality(String),
    source_time         DateTime64(3, 'Europe/Moscow'),
    trade_no            Int64,
    board_id            LowCardinality(Nullable(String)),
    price               Nullable(Float64),
    quantity            Nullable(Int64),
    value               Nullable(Float64),
    period              LowCardinality(Nullable(String)),
    buy_sell            LowCardinality(Nullable(String)),
    decimals            Nullable(Int32),
    trading_session     LowCardinality(Nullable(String)),
    trade_session_date  Nullable(Date),
    systime             Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority     UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYYYYMM(source_time)
ORDER BY (secid, source_time, trade_no)
SETTINGS non_replicated_deduplication_window = 1000;


CREATE TABLE moex_trades_futures
(
    secid               LowCardinality(String),
    source_time         DateTime64(3, 'Europe/Moscow'),
    trade_no            Int64,
    board_name          LowCardinality(Nullable(String)),
    price               Nullable(Float64),
    quantity            Nullable(Int64),
    rec_no              Nullable(Int64),
    open_position       Nullable(Int64),
    off_market_deal     Nullable(Int32),
    buy_sell            LowCardinality(Nullable(String)),
    trade_session_date  Nullable(Date),
    systime             Nullable(DateTime64(3, 'Europe/Moscow')),
    ingest_priority     UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toYYYYMM(source_time)
ORDER BY (secid, source_time, trade_no)
SETTINGS non_replicated_deduplication_window = 1000;


CREATE TABLE moex_orderbook
(
    secid               LowCardinality(String),
    source_time         DateTime64(3, 'Europe/Moscow'),
    buy_sell            LowCardinality(String),
    price               Float64,
    board_id            LowCardinality(Nullable(String)),
    quantity            Nullable(Int64),
    decimals            Nullable(Int64),
    trade_session_date  Nullable(Date),
    ingest_priority     UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(ingest_priority)
PARTITION BY toDate(source_time)
ORDER BY (secid, source_time, buy_sell, price)
TTL toDateTime(source_time) + INTERVAL 30 DAY
SETTINGS non_replicated_deduplication_window = 1000;


-- ═══════════════════════════════════════════════════════════
-- РАЗДЕЛ 3. Статистическая семья ALGOPACK. Десять рядов.
--
-- Все на ReplacingMergeTree по версии ingested_at. Ключи сортировки прежние.
-- Столбец ingested_at заполняется значением по умолчанию: карты столбцов его
-- не передают, а исполнитель вставки указывает список столбцов явно, поэтому
-- правок кода перевод не требует.
-- ═══════════════════════════════════════════════════════════

-- Статистики сделок (TradeStats), акции, 5 минут.
-- Источник: AlgCandlesTradeStatSchema / SuperCandlesTradeStats5mDTO.
-- source_time собирается писателем из tradedate + tradetime.
CREATE TABLE moex_trade_stats_5m_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    pr_open      Nullable(Float64),
    pr_high      Nullable(Float64),
    pr_low       Nullable(Float64),
    pr_close     Nullable(Float64),
    pr_std       Nullable(Float64),
    vol          Nullable(Int32),
    val          Nullable(Float64),
    trades       Nullable(Int32),
    pr_vwap      Nullable(Float64),
    pr_change    Nullable(Float64),
    trades_b     Nullable(Int32),
    trades_s     Nullable(Int32),
    val_b        Nullable(Float64),
    val_s        Nullable(Float64),
    vol_b        Nullable(Int64),
    vol_s        Nullable(Int64),
    disb         Nullable(Float64),
    pr_vwap_b    Nullable(Float64),
    pr_vwap_s    Nullable(Float64),
    sec_pr_open  Nullable(Int32),
    sec_pr_high  Nullable(Int32),
    sec_pr_low   Nullable(Int32),
    sec_pr_close Nullable(Int32),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;


-- Статистики сделок (TradeStats), фьючерсы, 5 минут.
-- Источник: FuturesTradeStatsSchema / SuperCandlesFuturesTradeStats5mDTO.
CREATE TABLE moex_trade_stats_5m_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    pr_open      Nullable(Float64),
    pr_high      Nullable(Float64),
    pr_low       Nullable(Float64),
    pr_close     Nullable(Float64),
    pr_std       Nullable(Float64),
    vol          Nullable(Int64),
    val          Nullable(Int64),
    trades       Nullable(Int32),
    pr_vwap      Nullable(Float64),
    pr_change    Nullable(Float64),
    trades_b     Nullable(Int32),
    trades_s     Nullable(Int32),
    val_b        Nullable(Float64),
    val_s        Nullable(Float64),
    vol_b        Nullable(Int64),
    vol_s        Nullable(Int64),
    disb         Nullable(Float64),
    pr_vwap_b    Nullable(Float64),
    pr_vwap_s    Nullable(Float64),
    im           Nullable(Float64),
    oi_open      Nullable(Int64),
    oi_high      Nullable(Int64),
    oi_low       Nullable(Int64),
    oi_close     Nullable(Int64),
    sec_pr_open  Nullable(Int32),
    sec_pr_high  Nullable(Int32),
    sec_pr_low   Nullable(Int32),
    sec_pr_close Nullable(Int32),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;


-- Статистики стакана (OBStats), акции, 5 минут.
-- Источник: AlgOrderBookStats5mSchema / SuperCandlesOrderBookStats5mDTO.
CREATE TABLE moex_ob_stats_5m_stock
(
    secid              LowCardinality(String),
    source_time        DateTime64(3, 'Europe/Moscow'),
    spread_bbo         Nullable(Float64),
    spread_lv10        Nullable(Float64),
    spread_1mio        Nullable(Float64),
    levels_b           Nullable(Int32),
    levels_s           Nullable(Int32),
    vol_b              Nullable(Int64),
    vol_s              Nullable(Int64),
    val_b              Nullable(Int64),
    val_s              Nullable(Int64),
    imbalance_vol_bbo  Nullable(Float64),
    imbalance_val_bbo  Nullable(Float64),
    imbalance_vol      Nullable(Float64),
    imbalance_val      Nullable(Float64),
    vwap_b             Nullable(Float64),
    vwap_s             Nullable(Float64),
    vwap_b_1mio        Nullable(Float64),
    vwap_s_1mio        Nullable(Float64),
    systime            Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at        DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;


-- Статистики стакана (OBStats), фьючерсы, 5 минут.
-- Источник: AlgFuturesOrderBookSchema / SuperCandlesFuturesOrderBookStats5mDTO.
CREATE TABLE moex_ob_stats_5m_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    mid_price    Nullable(Float64),
    micro_price  Nullable(Float64),
    spread_l1    Nullable(Float64),
    spread_l2    Nullable(Float64),
    spread_l3    Nullable(Float64),
    spread_l5    Nullable(Float64),
    spread_l10   Nullable(Float64),
    spread_l20   Nullable(Float64),
    levels_b     Nullable(Int32),
    levels_s     Nullable(Int32),
    vol_b_l1     Nullable(Int64),
    vol_b_l2     Nullable(Int64),
    vol_b_l3     Nullable(Int64),
    vol_b_l5     Nullable(Int64),
    vol_b_l10    Nullable(Int64),
    vol_b_l20    Nullable(Int64),
    vol_s_l1     Nullable(Int64),
    vol_s_l2     Nullable(Int64),
    vol_s_l3     Nullable(Int64),
    vol_s_l5     Nullable(Int64),
    vol_s_l10    Nullable(Int64),
    vol_s_l20    Nullable(Int64),
    vwap_b_l3    Nullable(Float64),
    vwap_b_l5    Nullable(Float64),
    vwap_b_l10   Nullable(Float64),
    vwap_b_l20   Nullable(Float64),
    vwap_s_l3    Nullable(Float64),
    vwap_s_l5    Nullable(Float64),
    vwap_s_l10   Nullable(Float64),
    vwap_s_l20   Nullable(Float64),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;


-- Статистики заявок (OrderStats), акции, 5 минут. Фьючерсного варианта нет.
-- Источник: AlgOrderStats5mSchema / SuperCandlesOrderStats5mDTO.
-- Неоднородность типов сохранена намеренно: cancel_vol_s, cancel_vol и
-- cancel_orders — Int64, прочие объёмы — Int32.
CREATE TABLE moex_order_stats_5m_stock
(
    secid           LowCardinality(String),
    source_time     DateTime64(3, 'Europe/Moscow'),
    put_orders_b    Nullable(Int32),
    put_orders_s    Nullable(Int32),
    put_val_b       Nullable(Float64),
    put_val_s       Nullable(Float64),
    put_vol_b       Nullable(Int32),
    put_vol_s       Nullable(Int32),
    put_vwap_b      Nullable(Float64),
    put_vwap_s      Nullable(Float64),
    put_vol         Nullable(Int32),
    put_val         Nullable(Float64),
    put_orders      Nullable(Int32),
    cancel_orders_b Nullable(Int32),
    cancel_orders_s Nullable(Int32),
    cancel_val_b    Nullable(Float64),
    cancel_val_s    Nullable(Float64),
    cancel_vol_b    Nullable(Int32),
    cancel_vol_s    Nullable(Int64),
    cancel_vwap_b   Nullable(Float64),
    cancel_vwap_s   Nullable(Float64),
    cancel_vol      Nullable(Int64),
    cancel_val      Nullable(Float64),
    cancel_orders   Nullable(Int64),
    systime         Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at     DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time)
SETTINGS non_replicated_deduplication_window = 1000;


-- Открытый интерес (FUTOI), 5 минут. Только фьючерсы.
-- Источник: FutoiSchema / FutoiDTO. secid — поле ticker источника.
-- Несколько строк на момент по группам участников, поэтому clgroup в ключе.
CREATE TABLE moex_futoi_5m
(
    secid              LowCardinality(String),
    source_time        DateTime64(3, 'Europe/Moscow'),
    clgroup            LowCardinality(String),
    sess_id            Nullable(Int32),
    seqnum             Nullable(Int32),
    pos                Nullable(Int64),
    pos_long           Nullable(Int64),
    pos_short          Nullable(Int64),
    pos_long_num       Nullable(Int64),
    pos_short_num      Nullable(Int64),
    trade_session_date Nullable(String),
    systime            Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at        DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, clgroup)
SETTINGS non_replicated_deduplication_window = 1000;


-- Концентрация (HI2), акции, ежедневно. Источник: Hi2AssetSchema / Hi2AssetDTO.
-- Узкая форма: строка на показатель, поэтому metric в ключе и обязателен.
CREATE TABLE moex_hi2_daily_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    metric       LowCardinality(String),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 1000;


-- Концентрация (HI2), фьючерсы, ежедневно.
-- Источник: Hi2FuturesSchema / Hi2FuturesDTO.
CREATE TABLE moex_hi2_daily_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    metric       LowCardinality(String),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, metric)
SETTINGS non_replicated_deduplication_window = 1000;


-- Оповещения (MegaAlerts), акции. Событийный ряд, история с 2024 года.
-- Источник: MegaAlertsAssetSchema / MegaAlertsAssetsDTO.
-- reference — вложенная строка JSON со статистикой за 90 дней, не снимок
-- текущего состояния.
CREATE TABLE moex_megaalerts_stock
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    alert_type   LowCardinality(String),
    threshold    Nullable(Float64),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, alert_type)
SETTINGS non_replicated_deduplication_window = 1000;


-- Оповещения (MegaAlerts), фьючерсы.
-- Источник: MegaAlertsFuturesSchema / MegaAlertsFuturesDTO.
CREATE TABLE moex_megaalerts_futures
(
    secid        LowCardinality(String),
    source_time  DateTime64(3, 'Europe/Moscow'),
    asset_code   LowCardinality(Nullable(String)),
    alert_type   LowCardinality(String),
    threshold    Nullable(Float64),
    value        Nullable(Float64),
    reference    Nullable(String),
    systime      Nullable(DateTime64(3, 'Europe/Moscow')),
    ingested_at  DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(ingested_at)
PARTITION BY toYear(source_time)
ORDER BY (secid, source_time, alert_type)
SETTINGS non_replicated_deduplication_window = 1000;


-- ═══════════════════════════════════════════════════════════
-- РАЗДЕЛ 4. Представления чтения.
--
-- По одному на каждую схлопывающую таблицу. Читатели (витрина, аналитический
-- контур) обращаются к представлению, а не к таблице напрямую: так забытый
-- модификатор FINAL перестаёт быть возможной ошибкой.
--
-- Запись идёт ТОЛЬКО в таблицы, никогда в представления.
-- ═══════════════════════════════════════════════════════════

CREATE VIEW moex_candles_1m_final              AS SELECT * FROM moex_candles_1m FINAL;
CREATE VIEW moex_candles_10m_final             AS SELECT * FROM moex_candles_10m FINAL;
CREATE VIEW moex_candles_1h_final              AS SELECT * FROM moex_candles_1h FINAL;
CREATE VIEW moex_candles_1d_final              AS SELECT * FROM moex_candles_1d FINAL;

CREATE VIEW moex_trades_stock_final            AS SELECT * FROM moex_trades_stock FINAL;
CREATE VIEW moex_trades_futures_final          AS SELECT * FROM moex_trades_futures FINAL;
CREATE VIEW moex_orderbook_final               AS SELECT * FROM moex_orderbook FINAL;

CREATE VIEW moex_trade_stats_5m_stock_final    AS SELECT * FROM moex_trade_stats_5m_stock FINAL;
CREATE VIEW moex_trade_stats_5m_futures_final  AS SELECT * FROM moex_trade_stats_5m_futures FINAL;
CREATE VIEW moex_ob_stats_5m_stock_final       AS SELECT * FROM moex_ob_stats_5m_stock FINAL;
CREATE VIEW moex_ob_stats_5m_futures_final     AS SELECT * FROM moex_ob_stats_5m_futures FINAL;
CREATE VIEW moex_order_stats_5m_stock_final    AS SELECT * FROM moex_order_stats_5m_stock FINAL;
CREATE VIEW moex_futoi_5m_final                AS SELECT * FROM moex_futoi_5m FINAL;
CREATE VIEW moex_hi2_daily_stock_final         AS SELECT * FROM moex_hi2_daily_stock FINAL;
CREATE VIEW moex_hi2_daily_futures_final       AS SELECT * FROM moex_hi2_daily_futures FINAL;
CREATE VIEW moex_megaalerts_stock_final        AS SELECT * FROM moex_megaalerts_stock FINAL;
CREATE VIEW moex_megaalerts_futures_final      AS SELECT * FROM moex_megaalerts_futures FINAL;
