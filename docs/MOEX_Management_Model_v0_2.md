# Управляющая модель MOEX

Версия 0.2. Первый срез управляющей PostgreSQL-модели. Правки перед DDL: `MOEX_DDL_Preflight_Patches_v0_5`. Описывает карточки инструментов, календарь, сессии, ограничения, связи, задания и диапазоны загрузок.

---

## 1. Граница документа

PostgreSQL хранит управляющую модель MOEX: инструменты, торговые режимы, связи, календарь, сессии, ограничения, изменения, задания и последние загруженные диапазоны. Карточка акции и карточка фьючерса являются собранными представлениями из этих таблиц.

Рыночные ряды (свечи, суперсвечи, FUTOI, HI2, MegaAlerts, сделки, стакан) в PostgreSQL не хранятся. Для них позже выбирается отдельное хранилище; вероятный кандидат — ClickHouse.

Каждая таблица имеет трассировку до источника: метод клиента, endpoint MOEX, DTO, поля.

---

## 2. Цель первого среза

Первый срез создаёт минимальную управляющую модель, достаточную для:

1. Сбора базовых карточек акций и фьючерсов.
2. Понимания, как инструмент торгуется (рынок, режим, сессии).
3. Знания календаря и приостановок.
4. Знания истории изменений атрибутов инструментов.
5. Создания задания на загрузку рыночных данных.
6. Фиксации последних успешно загруженных диапазонов.

Первый срез не строит полную карточку инструмента и не реализует пульт оператора. Он закладывает данные, из которых карточка собирается.

---

## 3. Общие правила хранения

### 3.1. Правило хранения времени

Все даты и времена из MOEX приходят как локальные значения (московское время) без явного часового пояса. Они хранятся без зоны:

- `date` — для дат.
- `time` — для времени без даты.
- `timestamp` (`timestamp without time zone`) — для даты+времени MOEX.

Поля времени системы проекта (`created_at`, `updated_at`, `last_success_at`) хранятся как `timestamptz`.

Если позже будет принято решение нормализовать MOEX-время в UTC, это фиксируется отдельным правилом и миграцией.

### 3.2. Правило типов для цен и денежных значений

Цены, денежные величины и параметры контрактов в справочных таблицах хранятся как `numeric`. Это управляющие данные с малым объёмом, где точность важнее скорости. `float8` допустим для аналитических рядов, но они не в PostgreSQL.

### 3.3. Правило первичных ключей

Суррогатные числовые ключи используют `bigint GENERATED ALWAYS AS IDENTITY`. Естественные ключи — `text` (secid, type_code). В первом срезе `secid` используется как PK в `moex_instruments`, потому что загружаются только TQBR-акции и RFUD-фьючерсы. При расширении рынков ключ пересматривается.

Для таблиц с UUID-ключами (задания загрузки) используется нативная функция PostgreSQL 18 `uuidv7()`. UUIDv7 содержит Unix-timestamp в старших 48 битах, что обеспечивает хронологическую сортировку и эффективную вставку в B-tree индекс без фрагментации. Генерация UUID происходит на стороне базы через `DEFAULT uuidv7()`, не на стороне приложения.

### 3.4. Правило дедупликации

Таблицы с данными из MOEX, загружаемыми повторно, имеют естественные уникальные ключи. Повторная загрузка выполняет upsert по естественному ключу. Пустой `secid` и `"-"` в полях сессий нормализуются в NULL при записи.

PostgreSQL 15+: для уникальных ключей с nullable полями используется `UNIQUE NULLS NOT DISTINCT`.

### 3.5. Правило направления связей

В `moex_instrument_relations` направление: source — производный инструмент или серия, target — базовый инструмент. Фьючерс → акция, опционная серия → базовый актив.

### 3.6. Возможности PostgreSQL 18

Docker-compose использует `postgres:18.4`. Первый срез использует следующие возможности PostgreSQL 18:

- `uuidv7()` — нативная генерация UUID версии 7 (RFC 9562). Значения упорядочены по времени, что лучше подходит для B-tree индексов по сравнению с полностью случайными UUIDv4. Используется как `DEFAULT uuidv7()` для PK в `moex_load_tasks`.
- `UNIQUE NULLS NOT DISTINCT` — уникальные ключи, в которых NULL считается равным NULL. Используется для дедупликации сессий, приостановок и изменений с nullable полями.
- `GENERATED ALWAYS AS IDENTITY` — стандартный способ генерации суррогатных bigint-ключей. Используется во всех таблицах с суррогатным PK.
- Асинхронный ввод-вывод (AIO) — подсистема параллельного чтения. Конфигурируется через `io_method=worker` и `io_workers=3` в docker-compose. Оставляется как штатная возможность PostgreSQL 18. В первом срезе объём данных мал, отдельного эффекта не ожидаем и не закладываем как требование производительности. На Linux с ядром 5.1+ можно переключить на `io_method=io_uring`.

### 3.7. Правило парсинга строковых дат и времени

Все поля DTO, приходящие как строка и хранимые как `date`, `time` или `timestamp`, парсятся при записи. Если значение не распарсилось, в таблицу пишется NULL, а в лог пишется событие с указанием поля, источника и исходного значения.

Правило распространяется на все строковые даты/время из MOEX: `SETTLEDATE`, `IMTIME`, даты экспираций, даты приостановок, время сессий.

Для полей, которые в DTO уже типизированы (`DateTime?`), парсинг выполнен парсером MOEX и дополнительное преобразование не требуется.

### 3.8. Правило жизненного цикла инструмента

Ежедневная перезагрузка справочника инструментов выполняется через UPSERT и не удаляет отсутствующие в свежей выдаче инструменты. Отсутствие инструмента в свежей выдаче не означает физическое удаление строки. Такие инструменты остаются в базе как исторические/делистнутые, потому что на них могут ссылаться задания, диапазоны загрузки и результаты прошлых операций.

Статус активности инструмента определяется полями карточки, а не физическим наличием или отсутствием строки.

---

## 4. Инвентаризация методов

### 4.1. ISS client (публичный доступ)

| Метод клиента | Endpoint MOEX | DTO | Целевая таблица | Роль | Документация MOEX |
|---|---|---|---|---|---|
| `GetInfoTradedStockAssets` | `/engines/stock/markets/shares/boards/tqbr/securities.json` | `StockSecurityDTO` | `moex_instruments`, `moex_stock_details` | базовая карточка акции | ISS securities |
| `GetInfoTradedFuturesAssets` | `/engines/futures/markets/forts/boards/RFUD/securities.json` | `FuturesSecurityDTO` | `moex_instruments`, `moex_futures_details` | базовая карточка фьючерса | ISS securities |

### 4.2. Calendar client (платный APIM)

| Метод клиента | Endpoint MOEX | DTO | Целевая таблица | Роль | Документация MOEX |
|---|---|---|---|---|---|
| `GetStockOffDays` | `/calendars/stock.json` | `CalendarOffDaysMarketDTO` | `moex_calendar_days` | календарь фондового рынка | ISS Calendar |
| `GetFuturesOffDays` | `/calendars/futures.json` | `CalendarOffDaysMarketDTO` | `moex_calendar_days` | календарь срочного рынка | ISS Calendar |
| `GetStockSessionWithTypes` | `/calendars/stock/session.json` | `CalendarStockSessionDTO`, `CalendarSessionTypeDTO` | `moex_trading_sessions`, `moex_session_types` | расписание акций | ISS Calendar |
| `GetFuturesSessionWithTypes` | `/calendars/futures/session.json` | `CalendarFuturesSessionDTO`, `CalendarSessionTypeDTO` | `moex_trading_sessions`, `moex_session_types` | расписание фьючерсов | ISS Calendar |
| `GetFuturesSecuritiesAll` | `/calendars/futures/securities.json` | `CalendarFortsContractDTO` | `moex_forts_contracts` | контракты FORTS | ISS Calendar |
| `GetFuturesSecuritiesAll` | `/calendars/futures/securities.json` | `CalendarOptionsSeriesDTO` | `moex_options_series` | опционные серии | ISS Calendar |
| `GetSuspended` | `/calendars/stock/securities/suspended/details.json` | `CalendarSuspendedDTO` | `moex_suspensions` | приостановки торгов | ISS Calendar |
| `GetSuspendedReasons` | `/calendars/stock/securities/suspended/details.json` | `CalendarSuspendedReasonDTO` | `moex_suspension_reasons` | расшифровка причин | ISS Calendar |
| `GetSecurityAttributes` | `/calendars/stock/securities/changes.json` | `CalendarSecurityAttributeDTO` | `moex_security_attributes` | справочник атрибутов | ISS Calendar |
| `GetSecurityChanges` | `/calendars/stock/securities/changes.json` | `CalendarSecurityChangeDTO` | `moex_security_changes` | история изменений | ISS Calendar |

### 4.3. Realtime REST client (платный APIM)

| Метод клиента | Endpoint MOEX | DTO | Целевая таблица | Роль | Документация MOEX |
|---|---|---|---|---|---|
| `GetMarketStatisticsStockSecuritiesAsync` | `/engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json?iss.only=securities` | `MarketStatisticsStockSecuritiesDTO` | `moex_stock_details` (обогащение) | STATUS, MINSTEP, ISIN, DECIMALS, LISTLEVEL, CURRENCYID, ISSUESIZE, SETTLEDATE | Real-time market data |
| `GetMarketStatisticsFuturesSecuritiesAsync` | `/engines/futures/markets/forts/boards/RFUD/securities/{ticker}.json?iss.only=securities` | `MarketStatisticsFuturesSecuritiesDTO` | `moex_futures_details` (обогащение) | BUYSELLFEE, SCALPERFEE, LASTSETTLEPRICE, SETTLEPRICE_CLR, IMTIME | Real-time market data |

Особенность: endpoint-ы per-ticker (один запрос на один инструмент). Для массового обогащения карточек потребуется цикл по списку инструментов из `moex_instruments`.

Обогащение применяется по паре SECID + BOARDID. В первом срезе boardid = TQBR для акций и RFUD для фьючерсов. Для фьючерсов boardid в первом срезе хранится константой `'RFUD'`; при обогащении MarketStatistics значение BOARDID используется для сверки. При несовпадении BOARDID с ожидаемым значением обновление не должно завершаться тихим no-op: пишется событие в лог с secid, ожидаемым boardid и фактически полученным boardid.

Правила парсинга: `MarketStatisticsStockSecuritiesDTO.SETTLEDATE` (string `yyyy-MM-dd`) парсится в `date`. `MarketStatisticsFuturesSecuritiesDTO.IMTIME` (string `yyyy-MM-dd HH:mm:ss`) парсится в `timestamp without time zone`. Если значение не распарсилось — поле пишется как NULL, событие фиксируется в логах.

Примечание: DTO MarketStatistics находятся в namespace `ProjectTraiding.Moex.Contracts.Dto.MarketStatistics`. Физическое расположение файлов в папке `Contracts/Dto/Algopack/` является техническим долгом.

### 4.4. Таблицы без метода-источника

| Таблица | Источник | Роль |
|---|---|---|
| `moex_instrument_relations` | автоматически из `asset_code` + вручную | связи акция ↔ фьючерс ↔ опционная серия |
| `moex_load_tasks` | оператор | задание на загрузку рыночных данных |
| `moex_loaded_ranges` | результат успешной загрузки | непрерывные загруженные диапазоны |
| `moex_broker_tariffs` | оператор, вручную | условия брокера для модели издержек |

### 4.5. Методы, которые не создают таблицы PostgreSQL в первом срезе

| Метод / источник | DTO | Почему не сохраняем | Документация MOEX |
|---|---|---|---|
| `GetOffDaysAll` | `CalendarOffDaysAllDTO` | дублирует stock + futures offdays; используется только для сверки/диагностики | ISS Calendar |
| `GetCandles` | `CandlesDTO` | рыночный ряд, хранится вне PostgreSQL | ALGOPACK Super Candles |
| `GetSuperCandlesTradeStats5m` | `SuperCandlesTradeStats5mDTO` | рыночный ряд | ALGOPACK Super Candles |
| `GetSuperCandlesFuturesTradeStats5m` | `SuperCandlesFuturesTradeStats5mDTO` | рыночный ряд | ALGOPACK Super Candles |
| `GetSuperCandlesOrderBookStats5m` | `SuperCandlesOrderBookStats5mDTO` | рыночный ряд | ALGOPACK Super Candles |
| `GetSuperCandlesFuturesOrderBookStats5m` | `SuperCandlesFuturesOrderBookStats5mDTO` | рыночный ряд | ALGOPACK Super Candles |
| `GetSuperCandlesOrderStats5m` | `SuperCandlesOrderStats5mDTO` | рыночный ряд | ALGOPACK Super Candles |
| `GetFutoi` | `FutoiDTO` | рыночный ряд | ALGOPACK FUTOI |
| `GetHi2Asset` / `GetHi2Futures` | `Hi2AssetDTO` / `Hi2FuturesDTO` | рыночный ряд | ALGOPACK HI2 |
| `GetMegaAlerts` / `GetMegaAlertsFutures` | `MegaAlertsAssetsDTO` / `MegaAlertsFuturesDTO` | рыночный ряд | ALGOPACK Mega Alerts |
| Realtime candles / trades / orderbook | существующие DTO | рыночные данные, не PostgreSQL | MOEX Real-time market data |
| Realtime MarketStatistics Marketdata | нет метода и DTO в коде | не сохраняем в PostgreSQL; это текущий снимок торговых данных (bid, offer, spread, last, numtrades), а не справочная карточка. Если потребуется — отдельное решение: snapshot-таблица, ClickHouse или только диагностический вызов | MOEX Real-time market data |

Ссылки на документацию MOEX:
- MOEX ISS reference: https://iss.moex.com/iss/reference/
- ISS Calendar: https://moexalgo.github.io/docs/description/calendar-iss
- ALGOPACK Super Candles: https://moexalgo.github.io/docs/description/supercandles
- ALGOPACK FUTOI: https://moexalgo.github.io/docs/description/futoi
- ALGOPACK HI2: https://moexalgo.github.io/docs/description/hi2
- ALGOPACK Mega Alerts: https://moexalgo.github.io/docs/description/megaalerts
- MOEX Real-time market data: https://moexalgo.github.io/docs/description/realtime

---

## 5. Таблицы первого среза

### 5.1. moex_instruments

Единый справочник инструментов. FK для всех остальных таблиц.

Источник: `GetInfoTradedStockAssets` → `StockSecurityDTO`, `GetInfoTradedFuturesAssets` → `FuturesSecurityDTO`.

| Поле | Тип | Источник | Назначение |
|---|---|---|---|
| `secid` | text, PK | `SECID` | SBER, SiM6 |
| `instrument_type` | text, not null | по источнику | `stock` или `futures` |
| `asset_code` | text, nullable | `FuturesSecurityDTO.ASSETCODE` | Si, BR — только для фьючерсов |
| `shortname` | text | `.SHORTNAME` | короткое название |
| `secname` | text | `.SECNAME` | полное название |
| `updated_at` | timestamptz | при записи | когда карточка обновлена |

### 5.2. moex_stock_details

Параметры акции. Один к одному с `moex_instruments` для `instrument_type = stock`.

Источник: `GetInfoTradedStockAssets` → `StockSecurityDTO` (базовая карточка), `GetMarketStatisticsStockSecuritiesAsync` → `MarketStatisticsStockSecuritiesDTO` (обогащение).

| Поле | Тип | DTO-поле | MOEX столбец | Источник |
|---|---|---|---|---|
| `secid` | text, PK, FK → moex_instruments | `SECID` | SECID | ISS |
| `boardid` | text | `BOARDID` | BOARDID | ISS |
| `marketcode` | text | `MARKETCODE` | MARKETCODE | ISS |
| `lotsize` | int | `LOTSIZE` | LOTSIZE | ISS |
| `facevalue` | numeric | `FACEVALUE` | FACEVALUE | ISS |
| `prev_close_price` | numeric | `PREVLEGALCLOSEPRICE` | PREVLEGALCLOSEPRICE | ISS |
| `prev_date` | date | `PREVDATE` | PREVDATE | ISS |
| `status` | text, nullable | `STATUS` | STATUS | MarketStatistics |
| `decimals` | int, nullable | `DECIMALS` | DECIMALS | MarketStatistics |
| `minstep` | numeric, nullable | `MINSTEP` | MINSTEP | MarketStatistics |
| `isin` | text, nullable | `ISIN` | ISIN | MarketStatistics |
| `currency_id` | text, nullable | `CURRENCYID` | CURRENCYID | MarketStatistics |
| `list_level` | int, nullable | `LISTLEVEL` | LISTLEVEL | MarketStatistics |
| `issue_size` | bigint, nullable | `ISSUESIZE` | ISSUESIZE | MarketStatistics |
| `settle_date` | date, nullable | `SETTLEDATE` | SETTLEDATE | MarketStatistics |
| `updated_at` | timestamptz | при записи | — | — |

Поля из MarketStatistics nullable, потому что заполняются отдельным вызовом. `minstep` критичен для расчёта проскальзывания.

В первом срезе одна карточка акции хранится для одной основной доски TQBR. PK = secid. При добавлении нескольких boards ключ пересматривается на secid + boardid или вводится отдельная таблица листингов.

### 5.3. moex_futures_details

Параметры фьючерса. Один к одному с `moex_instruments` для `instrument_type = futures`.

Источник: `GetInfoTradedFuturesAssets` → `FuturesSecurityDTO` (базовая карточка), `GetMarketStatisticsFuturesSecuritiesAsync` → `MarketStatisticsFuturesSecuritiesDTO` (обогащение).

| Поле | Тип | DTO-поле | MOEX столбец | Источник |
|---|---|---|---|---|
| `secid` | text, PK, FK → moex_instruments | `SECID` | SECID | ISS |
| `boardid` | text, not null | константа `'RFUD'` | BOARDID | ISS (константа); MarketStatistics (сверка) |
| `initial_margin` | numeric | `INITIALMARGIN` | INITIALMARGIN | ISS |
| `prev_settle_price` | numeric | `PREVSETTLEPRICE` | PREVSETTLEPRICE | ISS |
| `prev_price` | numeric | `PREVPRICE` | PREVPRICE | ISS |
| `minstep` | numeric | `MINSTEP` | MINSTEP | ISS |
| `stepprice` | numeric | `STEPPRICE` | STEPPRICE | ISS |
| `lotvolume` | int | `LOTVOLUME` | LOTVOLUME | ISS |
| `decimals` | int | `DECIMALS` | DECIMALS | ISS |
| `last_trade_date` | date | `LASTTRADEDATE` | LASTTRADEDATE | ISS |
| `last_del_date` | date | `LASTDELDATE` | LASTDELDATE | ISS |
| `prev_open_position` | bigint | `PREVOPENPOSITION` | PREVOPENPOSITION | ISS |
| `high_limit` | numeric | `HIGHLIMIT` | HIGHLIMIT | ISS |
| `low_limit` | numeric | `LOWLIMIT` | LOWLIMIT | ISS |
| `buysell_fee` | numeric, nullable | `BUYSELLFEE` | BUYSELLFEE | MarketStatistics |
| `scalper_fee` | numeric, nullable | `SCALPERFEE` | SCALPERFEE | MarketStatistics |
| `last_settle_price` | numeric, nullable | `LASTSETTLEPRICE` | LASTSETTLEPRICE | MarketStatistics |
| `settle_price_clr` | numeric, nullable | `SETTLEPRICE_CLR` | SETTLEPRICE_CLR | MarketStatistics |
| `im_time` | timestamp, nullable | `IMTIME` | IMTIME | MarketStatistics |
| `updated_at` | timestamptz | при записи | — | — |

Поля из MarketStatistics nullable, потому что заполняются отдельным вызовом. `buysell_fee` и `scalper_fee` критичны для модели издержек (Первая вертикаль: «решение без учёта комиссии запрещено»).

В первом срезе одна карточка фьючерса хранится для основной доски RFUD. PK = secid. При базовой загрузке boardid пишется константой `'RFUD'` (`FuturesSecurityDTO` не содержит `BOARDID`). При обогащении значение BOARDID из MarketStatistics используется для сверки. Если появятся несколько досок для одного secid, PK пересматривается на `(secid, boardid)`.

### 5.4. moex_forts_contracts

Справочник FORTS-контрактов. Обогащает `moex_futures_details` данными об экспирации.

FK на `moex_instruments` не ставится. Таблица может содержать неторгуемые и исторические контракты, которых нет в текущем списке торгуемых инструментов.

Источник: `GetFuturesSecuritiesAll` → `CalendarFortsContractDTO`.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `secid` | text, PK | `SecId` | secid |
| `asset_code` | text | `AssetCode` | asset_code |
| `shortname` | text | `ShortName` | shortname |
| `exec_type` | text | `ExecType` | exec_type |
| `contract_name` | text | `ContractName` | contract_name |
| `expiration_date` | date | `ExpirationDate` | expiration_date |
| `end_date` | date | `EndDate` | end_date |
| `expiration_type` | text | `ExpirationType` | expiration_type |
| `expiration_time` | time | `ExpirationTime` | expiration_time |
| `weekend_session` | int | `WeekendSession` | weekend_session |
| `updated_at` | timestamptz | при записи | — |

### 5.5. moex_options_series

Опционные серии. Привязаны к базовому активу.

Источник: `GetFuturesSecuritiesAll` → `CalendarOptionsSeriesDTO`.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `series_name` | text, PK | `SeriesName` | series_name |

В первом срезе `series_name` используется как PK. Это рабочее допущение: уникальность `series_name` в выдаче MOEX не подтверждена. Если при первой загрузке обнаружится нарушение уникальности, выбор ключа пересматривается до записи данных.

| `asset_type_name` | text | `AssetTypeName` | asset_type_name |
| `asset_code` | text | `AssetCode` | asset_code |
| `series_type` | text | `SeriesType` | series_type |
| `exec_type` | text | `ExecType` | exec_type |
| `margin_style` | text | `MarginStyle` | margin_style |
| `contract_name` | text | `ContractName` | contract_name |
| `expiration_date` | date | `ExpirationDate` | expiration_date |
| `expiration_type` | text | `ExpirationType` | expiration_type |
| `expiration_time` | time | `ExpirationTime` | expiration_time |
| `weekend_session` | int | `WeekendSession` | weekend_session |
| `updated_at` | timestamptz | при записи | — |

### 5.6. moex_calendar_days

Торговый календарь: выходные, праздники, weekend-сессии. Одна таблица на оба рынка.

Источник: `GetStockOffDays` / `GetFuturesOffDays` → `CalendarOffDaysMarketDTO`.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `trade_date` | date, PK | `TradeDate` | tradedate |
| `market` | text, PK | по endpoint | `stock` или `futures` |
| `is_traded` | int | `IsTraded` | is_traded |
| `trade_session_date` | date, nullable | `TradeSessionDate` | trade_session_date |
| `reason` | text | `Reason` | reason |
| `moex_update_time` | timestamp | `UpdateTime` | updatetime |
| `updated_at` | timestamptz | при записи | — |

Значения `reason`: `H` — праздник (биржа закрыта), `W` — выходной с торгами в weekend-сессии.

Upsert по: `trade_date` + `market`.

### 5.7. moex_trading_sessions

Расписание торговых сессий.

Источник: `GetStockSessionWithTypes` → `CalendarStockSessionDTO`, `GetFuturesSessionWithTypes` → `CalendarFuturesSessionDTO`.

| Поле | Тип | DTO-поле stock | DTO-поле futures | MOEX столбец |
|---|---|---|---|---|
| `id` | bigint, PK, IDENTITY | — | — | суррогатный |
| `market` | text | — | — | `stock` или `futures` |
| `session_date` | date | `TradeDate` | `TradeSessionDate` | tradedate / trade_session_date |
| `trading_session` | int, nullable | `TradingSession` | null | tradingsession |
| `boardid` | text | `BoardId` | `BoardId` | boardid |
| `secid` | text, nullable | `SecId` (пустой → NULL) | `SecId` ("-" → NULL) | secid |
| `session_type` | text | `Type` | `Type` | type |
| `time_from` | timestamp | TradeDate + TimeFrom | `TimeFrom` | time_from |
| `time_till` | timestamp, nullable | TradeDate + TimeTill | `TimeTill` | time_till |
| `moex_update_time` | timestamp | `UpdateTime` | `UpdateTime` | updatetime |

При записи stock-сессии `TradeDate` + `TimeFrom` (time) собираются в `timestamp`. Для фьючерсов `TimeFrom` уже `DateTime?` и пишется в `time_from` напрямую как `timestamp`. Пустой `secid` и `"-"` нормализуются в NULL.

Upsert по: `UNIQUE NULLS NOT DISTINCT (market, session_date, boardid, secid, session_type, time_from)`.

### 5.8. moex_session_types

Справочник типов торговых сессий. Данные из MOEX.

Источник: `GetStockSessionWithTypes` / `GetFuturesSessionWithTypes` → `CalendarSessionTypeDTO`.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `type_code` | text, PK | `Type` | type |
| `market` | text, PK | по endpoint | `stock` или `futures` |
| `title` | text | `Title` | title |

Upsert по: `type_code` + `market`.

### 5.9. moex_suspensions

Приостановки торгов по инструментам.

Источник: `GetSuspended` → `CalendarSuspendedDTO`. Cursor-пагинация, до 160k+ записей.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `id` | bigint, PK, IDENTITY | — | суррогатный |
| `secid` | text | `SecId` | secid |
| `reason_id` | text | `ReasonId` | reason_id |
| `date_from` | date | `DateFrom` | date_from |
| `date_till` | date, nullable | `DateTill` | date_till |
| `boardid` | text, nullable | `BoardId` | boardid |
| `settle_codes` | text, nullable | `SettleCodes` | settle_codes |
| `change_date` | date | `ChangeDate` | changedate |
| `moex_update_time` | timestamp | `UpdateTime` | updatetime |

Естественный ключ: `secid` + `reason_id` + `date_from` + `date_till` + `boardid` + `settle_codes`. Для nullable полей (`date_till`, `boardid`, `settle_codes`) — `UNIQUE NULLS NOT DISTINCT`. Upsert по естественному ключу.

Связь с `moex_suspension_reasons` по `reason_id` — логическая, FK не ставится. Связь с `moex_instruments` по `secid` — логическая, FK не ставится. Причина: приостановки могут относиться к инструментам и причинам, которые не входят в текущий справочник первого среза.

### 5.10. moex_suspension_reasons

Справочник причин приостановок. 28 записей.

Источник: `GetSuspendedReasons` → `CalendarSuspendedReasonDTO`.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `reason_id` | text, PK | `Id`?.ToString() | id |
| `title` | text | `Title` | title |

`reason_id` хранится как text в обеих таблицах, потому что MOEX metadata определяет его как string, хотя значения выглядят числовыми.

### 5.11. moex_security_attributes

Справочник атрибутов, которые могут изменяться. 23 записи.

Источник: `GetSecurityAttributes` → `CalendarSecurityAttributeDTO`.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `name` | text, PK | `Name` | name |
| `data_type` | text | `Type` | type |
| `title` | text | `Title` | title |

Значения `data_type`: `D` — date, `I` — integer, `N` — numeric, `S` — string, `B` — boolean.

### 5.12. moex_security_changes

История изменений атрибутов инструментов.

Источник: `GetSecurityChanges` → `CalendarSecurityChangeDTO`. Cursor-пагинация.

| Поле | Тип | DTO-поле | MOEX столбец |
|---|---|---|---|
| `id` | bigint, PK, IDENTITY | — | суррогатный |
| `moex_update_time` | timestamp | `UpdateTime` | updatetime |
| `action` | text | `Action` | action |
| `secid` | text | `SecId` | secid |
| `attribute_name` | text | `AttributeName` | attribute_name |
| `before_value` | text, nullable | `BeforeValue` | before_value |
| `after_value` | text, nullable | `AfterValue` | after_value |

Значения `action`: `updated` — атрибут изменился, `removed` — атрибут удалён (after_value = null).

Дедупликация при повторной загрузке по: `moex_update_time` + `action` + `secid` + `attribute_name` + `before_value` + `after_value`. Для nullable полей — `UNIQUE NULLS NOT DISTINCT`.

Связь с `moex_instruments` по `secid` — логическая, FK не ставится. Связь с `moex_security_attributes` по `attribute_name` — логическая, FK не ставится.

### 5.13. moex_instrument_relations

Связи между инструментами.

| Поле | Тип | Назначение |
|---|---|---|
| `id` | bigint, PK, IDENTITY | суррогатный |
| `source_secid` | text, not null, FK → moex_instruments | производный инструмент (SiM6) |
| `target_secid` | text, nullable, FK → moex_instruments | базовый инструмент (SBER), nullable для asset_code-связей |
| `target_asset_code` | text, nullable | базовый актив цели (Si), nullable для прямых связей |
| `relation_type` | text, not null | тип связи |
| `confidence` | text, not null | `auto` или `manual` |
| `comment` | text, nullable | пояснение для ручных связей |
| `created_at` | timestamptz | когда создана |

Ограничения: `CHECK (target_secid IS NOT NULL OR target_asset_code IS NOT NULL)`. Уникальность: `UNIQUE NULLS NOT DISTINCT (source_secid, target_secid, target_asset_code, relation_type)`.

FK: `source_secid` и `target_secid` — жёсткие FK на `moex_instruments`. `target_asset_code` — без FK, потому что `asset_code` нигде не PK. Отдельный индекс на `source_secid` не создаётся: его покрывает UNIQUE, где `source_secid` — ведущая колонка. Индекс на `target_secid` создаётся частичным: `WHERE target_secid IS NOT NULL`.

Направление: source — производный, target — базовый. Типы связей: `future_underlying` (фьючерс → акция/актив), `same_underlying` (два фьючерса на один актив), `manual_related`.

Опционные серии в первом срезе связаны с базовым активом через `asset_code` в `moex_options_series`, без записи в `moex_instrument_relations`. Тип `option_underlying` добавляется когда появится потребность в явных связях серий с инструментами.

Правило генерации связей: если `asset_code` фьючерса совпал с `secid` акции, создаётся только точная строка связи с `target_secid = <акция>`. Обобщённая строка `target_secid = NULL` для такого фьючерса не создаётся.

### 5.14. moex_load_tasks

Задания на загрузку рыночных данных. Создаётся оператором. В первом срезе совмещает задание и один запуск. Если появятся повторы и ретраи, добавляется `moex_load_runs`.

В первом срезе `moex_load_tasks` предназначен для исторических и пакетных загрузок через ALGOPACK. Realtime debug endpoints не создают load_tasks.

| Поле | Тип | Назначение |
|---|---|---|
| `id` | uuid, PK, DEFAULT uuidv7() | идентификатор задания, генерируется базой |
| `secid` | text, not null, FK → moex_instruments | инструмент |
| `market` | text | `stock` или `futures` |
| `boardid` | text | TQBR, RFUD |
| `data_kind` | text | `candles`, `tradestats`, `obstats`, `orderstats`, `futoi`, `hi2`, `mega_alerts` |
| `candle_interval` | int, nullable | интервал свечей (1, 5, 60), только для candles |
| `date_from` | date | начало периода |
| `date_till` | date | конец периода |
| `status` | text | `pending`, `running`, `done`, `error` |
| `stop_reason` | text, nullable | причина остановки: `empty_cursor`, `range_exhausted`, `safety_cap_hit` |
| `rows_loaded` | bigint | количество загруженных строк |
| `storage_target` | text | `none` или `file` в первом срезе; `clickhouse` допустимо, но не используется до появления ClickHouse |
| `created_at` | timestamptz | когда создано |
| `started_at` | timestamptz, nullable | когда начата загрузка |
| `finished_at` | timestamptz, nullable | когда завершена |
| `error_message` | text, nullable | текст ошибки при status = error |

### 5.15. moex_loaded_ranges

Непрерывные успешно загруженные диапазоны. Одна строка = один непрерывный диапазон для комбинации инструмент + тип данных + интервал + boardid + storage_target.

| Поле | Тип | Назначение |
|---|---|---|
| `id` | bigint, PK, IDENTITY | суррогатный |
| `secid` | text | инструмент |
| `market` | text | `stock` или `futures` |
| `boardid` | text | TQBR, RFUD |
| `data_kind` | text | тип данных |
| `candle_interval` | int, nullable | интервал (для candles) |
| `date_from` | date | начало диапазона |
| `date_till` | date | конец диапазона |
| `last_success_at` | timestamptz | время последней успешной загрузки |
| `last_task_id` | uuid, nullable, FK → moex_load_tasks | ссылка на последнее задание |
| `rows_total` | bigint | общее количество строк в диапазоне |
| `storage_target` | text | `none`, `file`; `clickhouse` позже |
| `status` | text | `ok`, `partial`, `stale` |

Уникальность: `secid` + `market` + `boardid` + `data_kind` + `candle_interval` + `date_from` + `date_till` + `storage_target`.

`storage_target = 'none'` означает: проверено, что MOEX отдаёт данные за период, но данные нигде не сохранены. Такой диапазон не даёт права считать его доступным для расчёта признаков. Витрина признаков строится только поверх диапазонов с реально сохранёнными данными (`'file'`, `'clickhouse'`).

### 5.16. moex_broker_tariffs

Условия брокера. Вводятся оператором вручную. Не загружаются из MOEX.

Полная модель издержек для одной сделки: биржевая комиссия (`buysell_fee` / `scalper_fee` из `moex_futures_details`) + брокерская комиссия (из этой таблицы) + спред (из рыночных данных позже) + проскальзывание (из `minstep`). Первая вертикаль: «решение по свечам без учёта комиссии, спреда и проскальзывания запрещено».

| Поле | Тип | Назначение |
|---|---|---|
| `id` | bigint, PK, IDENTITY | суррогатный |
| `broker_name` | text | BCS, Tinkoff, Finam |
| `tariff_name` | text | Трейдер, Инвестор |
| `market` | text | `stock` или `futures` |
| `fee_type` | text | тип комиссии |
| `fee_value` | numeric | значение: 0.01 (процент) или 3.50 (руб./контракт) |
| `fee_currency` | text | RUB |
| `min_fee` | numeric, nullable | минимальная комиссия за сделку, если есть |
| `turnover_threshold` | numeric, nullable | порог оборота для ступенчатых тарифов |
| `valid_from` | date | с какой даты действует |
| `valid_till` | date, nullable | до какой даты, null = текущий |
| `comment` | text, nullable | пояснение оператора |
| `created_at` | timestamptz | когда внесено |
| `updated_at` | timestamptz | когда изменено |

Значения `fee_type`: `percent_of_turnover` (процент от оборота, типично для акций), `fixed_per_contract` (фиксированная сумма за контракт, типично для фьючерсов), `fixed_per_trade` (фиксированная за сделку), `monthly` (абонентская плата), `depository` (депозитарий).

Одна строка = одна комиссионная составляющая. Для одного тарифа обычно несколько строк: процент на акциях, ставка на фьючерсах, депозитарий.

---

## 6. Что не создаётся сейчас

| Сущность | Причина |
|---|---|
| `moex_boards` | TQBR и RFUD — известные режимы, справочник бордов не загружается |
| `moex_data_quality_checks` | нет записи рыночных данных, нечего проверять |
| `moex_request_templates` | endpoint-ы формируются в коде |
| `moex_load_runs` | в первом срезе один task = один запуск |
| Realtime MarketStatistics Marketdata | в коде нет метода и DTO для marketdata-блока; это текущий снимок торговых данных, а не справочная карточка. MarketStatistics Securities уже входит в первый срез как обогащение карточек |
| Таблицы рыночных рядов | хранятся вне PostgreSQL |

Источник истины для даты экспирации — `moex_forts_contracts` (Calendar): `expiration_date`, `expiration_type`, `expiration_time`. Поля `futures_details.last_trade_date` / `last_del_date` остаются полями карточки из ISS. Расхождение между Calendar и ISS фиксируется как событие проверки качества в фазе 4. Оно не меняет источник истины для сценариев экспирации.

---

## 7. Сценарии использования

Все сценарии работают на 16 таблицах первого среза без рыночных рядов.

### 7.1. Инструменты и карточки

1. Показать все акции TQBR, отсортировать по уровню листинга.
2. Показать все фьючерсы на один базовый актив (Si) — все живые контракты с датами экспирации.
3. Найти ближний контракт по базовому активу (первый с экспирацией > сегодня).
4. Предупредить: до экспирации ближнего контракта осталось N дней, пора переключаться.
5. Показать карточку акции: лот, шаг цены, ISIN, валюта, статус торгов.
6. Показать карточку фьючерса: ГО, шаг, цена шага, лимиты, комиссии биржи.
7. Найти инструменты, у которых обогащение из MarketStatistics ещё не выполнялось (nullable поля).
8. Показать связи: по SBER → какие фьючерсы привязаны.
9. Показать обратную связь: по фьючерсу SiM6 → какой базовый актив, есть ли связанная акция.
10. Найти инструменты без связей — кандидаты на ручную привязку.
11. Показать инструменты, у которых сменился атрибут за последнюю неделю.
12. Показать новые инструменты — появились в справочнике, но ещё не было загрузок.

### 7.2. Календарь и расписание

13. Сегодня торговый день на фондовом рынке? На срочном?
14. Показать ближайшие нерабочие дни: праздники (H) отдельно, выходные с weekend-сессией (W) отдельно.
15. Сколько торговых дней в заданном периоде для stock? Для futures?
16. Показать расписание сессий на сегодня: аукцион открытия, основная, вечерняя, клиринг.
17. Есть ли у конкретного инструмента индивидуальное расписание (secid непустой в sessions)?
18. Показать все weekend-сессии за месяц — когда биржа работала в выходные.
19. Когда следующий клиринг на срочном рынке?

### 7.3. Приостановки и ограничения

20. Показать все активные приостановки (date_till = null или > сегодня).
21. Есть ли приостановка по конкретному инструменту на конкретную дату?
22. Перед загрузкой проверить: инструмент не приостановлен за запрашиваемый период.
23. Показать историю приостановок инструмента — когда был приостановлен, когда возобновлён.
24. Показать расшифровку причины приостановки по reason_id.
25. Сколько инструментов приостановлено прямо сейчас?

### 7.4. Изменения атрибутов

26. Показать все изменения за последние сутки — какие инструменты затронуты.
27. Фильтр по типу атрибута: только изменения купонных дат, или только переименования SECID.
28. Предупреждение: инструмент, по которому идёт загрузка, изменил атрибуты.

### 7.5. Загрузки — создание заданий

29. Оператор выбирает инструмент + тип данных + период → создаётся задание.
30. Перед созданием система проверяет: период не содержит только нерабочие дни.
31. Перед созданием система проверяет: инструмент не приостановлен за весь период.
32. Массовая загрузка: выбрать группу инструментов → пачка заданий.
33. Показать все задания в статусе pending.
34. Показать все задания в статусе error — с текстом ошибки.
35. Перезапустить упавшее задание (создать новое с теми же параметрами).
36. Показать задания за сегодня — сколько создано, выполнено, строк загружено.

### 7.6. Загрузки — мониторинг и полнота

37. По инструменту показать все загруженные диапазоны: типы данных, периоды.
38. Найти дыры: ожидалось N торговых дней, загружено M — какие пропущены (calendar_days × loaded_ranges). Примечание: `moex_calendar_days` хранит только нерабочие и особые дни. Ожидаемые торговые дни за период считаются через `generate_series` по периоду минус off-days. Таблица не является полным календарём всех торговых дней.
39. Показать инструменты с устаревшей загрузкой (last_success_at > N дней назад).
40. Показать инструменты без загрузок.
41. Сравнить два инструмента по полноте загрузки.
42. Общая статистика: сколько инструментов загружено, строк всего, типов данных покрыто.

### 7.7. Модель издержек

43. Полная комиссия за сделку: биржевая (buysell_fee) + брокерская (broker_tariffs).
44. Сравнить издержки по инструментам: SiM6 vs BRN6 vs SBER-фьючерс.
45. История тарифов брокера: когда менялась комиссия.
46. Обновление тарифа: старая строка получает valid_till, новая — valid_from.
47. Round-trip издержки: 2 × (биржевая + брокерская) + спред × лот.
48. При смене брокера или тарифа — пересчитать оценки издержек.

### 7.8. Фьючерсы — специфика

49. Все контракты FORTS по базовому активу с датами экспирации — таблица ролла.
50. Какие контракты экспирируются в ближайшие 30 дней?
51. Есть ли weekend-сессия у контракта (поле в forts_contracts)?
52. Опционные серии по базовому активу.
53. FUTOI загружается по asset_code (Si), не по secid (SiM6) — система маппит правильно.

### 7.9. Выбор инструмента для первой модели

54. Рейтинг по объёму загруженных данных (rows_total из loaded_ranges).
55. Рейтинг по полноте: у кого загружено больше типов данных.
56. Рейтинг по издержкам: самая низкая полная комиссия.
57. Рейтинг по ликвидности: prev_open_position для фьючерсов, issue_size для акций.
58. Сводная таблица: инструмент × (полнота, издержки, ликвидность) → помощь в выборе.

### 7.10. Dashboard здоровья

59. Сколько инструментов в базе, сколько с обогащением, сколько без.
60. Сколько торговых дней в календаре, сколько загружено суммарно.
61. Задания по статусам (pending / running / done / error).
62. Ближайшая экспирация фьючерса — предупреждение.
63. Сколько активных приостановок.
64. Когда последний раз обновлялись справочники (updated_at).
65. Текущий тариф брокера и условия.

### 7.11. Регулярные операции

66. Ежедневное обновление справочников: перезагрузить instruments из ISS.
67. Ежедневное обновление календаря: offdays и sessions на ближайший период.
68. Периодическое обновление MarketStatistics: пройти по инструментам, обновить комиссии и ГО.
69. Периодическое обновление приостановок.
70. Автоматическое создание задания загрузки: каждый вечер после закрытия сессии — свечи за сегодня.

---

## 8. Связь с другими документами

- **Обзор системы** — раздел 5.1 (стек: PostgreSQL — управляющая база), раздел 5.3 (подключения к данным по необходимости), раздел 4.2 (контур хранения и качества).
- **Правила разработки** — правило 10 (без универсальных хранилищ), правило 8 (без интерфейсов на одну реализацию).
- **Контракт источника MOEX** — раздел 7 (классы клиентов и методы), раздел 17 (события журнала).
- **Роадмап** — фаза 4 (хранение и качество данных).
- **Первая продуктовая вертикаль** — раздел 4 (участок «хранение и проверка качества»).
- **Цепочка загрузки** — `MOEX_Loading_Chain_v0_1` описывает порядок загрузки управляющей модели.
- **Preflight patches** — `MOEX_DDL_Preflight_Patches_v0_5` фиксирует правки перед DDL.
