# Цепочка загрузки управляющей модели MOEX

Версия 0.2. Порядок миграций = порядок загрузки = порядок вызова методов клиентов. Правки: `MOEX_DDL_Preflight_Patches_v0_5`.

---

## 1. Принцип

FK-зависимости таблиц определяют порядок вызова методов MOEX. Нельзя записать строку, если FK-цель ещё не существует. Нельзя обогатить карточку, если базовая строка не записана. Нельзя создать связь, если оба конца не в базе.

Каждый шаг ниже описывает: какая миграция создаёт таблицу, какой метод клиента вызывается, какой endpoint MOEX, какой DTO возвращается, какие поля пишутся в таблицу, какие индексы нужны и какие сценарии (раздел 7 модели) эти индексы обслуживают.

### Индексы первого среза

Структурно обязательные индексы входят в первую миграцию и не зависят от пользовательских сценариев:

1. Первичный ключ.
2. `UNIQUE` под дедупликацию и `ON CONFLICT` при повторной загрузке.
3. Индекс на дочерней колонке под каждый жёсткий FK (частичный для nullable FK).

Сценарные индексы ускоряют чтение по сценариям раздела 7. Они не входят в первую миграцию. Добавляются отдельными миграциями после загрузки данных реалистичного объёма и проверки фактического использования через `pg_stat_user_indexes` и `EXPLAIN ANALYZE`.

На маленьких справочниках (`moex_session_types`, `moex_suspension_reasons`, `moex_security_attributes`) сценарные индексы не создаются.

Влияние PostgreSQL 18 skip scan на состав составных индексов оценивается по факту замера, а не закладывается заранее как проектное решение.

Имена индексов задаются по единой конвенции: `idx_<table>_<column_or_purpose>`.

---

## 2. Волна 1 — Ядро: инструменты и карточки

### Шаг 1. moex_instruments

| | |
|---|---|
| Миграция | V001__create_moex_instruments.sql |
| Зависит от | ничего — это корень |
| Кто зависит | все остальные 15 таблиц |
| Режим доступа | публичный ISS (без ключа) |

**Вызов 1a: акции**

| | |
|---|---|
| Клиент | `MoexHttpIssClient` |
| Метод | `GetInfoTradedStockAssets()` |
| Endpoint | `GET /engines/stock/markets/shares/boards/tqbr/securities.json` |
| DTO | `List<StockSecurityDTO>` |
| Парсер | `ParsingIssUtf8.ParseIssSecurityStock` |

Маппинг полей в `moex_instruments`:

| DTO-поле | → Колонка таблицы | Преобразование |
|---|---|---|
| `SECID` | `secid` (PK) | as-is |
| — | `instrument_type` | константа `'stock'` |
| — | `asset_code` | `NULL` (у акций нет asset_code) |
| `SHORTNAME` | `shortname` | as-is |
| `SECNAME` | `secname` | as-is |
| — | `updated_at` | `now()` |

**Вызов 1b: фьючерсы**

| | |
|---|---|
| Клиент | `MoexHttpIssClient` |
| Метод | `GetInfoTradedFuturesAssets()` |
| Endpoint | `GET /engines/futures/markets/forts/boards/RFUD/securities.json` |
| DTO | `List<FuturesSecurityDTO>` |
| Парсер | `ParsingIssUtf8.ParseIssSecurityFutures` |

Маппинг полей в `moex_instruments`:

| DTO-поле | → Колонка таблицы | Преобразование |
|---|---|---|
| `SECID` | `secid` (PK) | as-is |
| — | `instrument_type` | константа `'futures'` |
| `ASSETCODE` | `asset_code` | as-is |
| `SHORTNAME` | `shortname` | as-is |
| `SECNAME` | `secname` | as-is |
| — | `updated_at` | `now()` |

**Upsert**: `INSERT ... ON CONFLICT (secid) DO UPDATE SET shortname, secname, asset_code, updated_at`. Ежедневная перезагрузка выполняется через UPSERT. `DELETE` по отсутствующим в свежей выдаче инструментам не выполняется.

**Структурные индексы этой миграции**:
- PK `secid` — сценарии 5, 6, 7, 8, 9: поиск инструмента по тикеру.

**Сценарные индексы (отдельной миграцией позже)**:
- `idx_moex_instruments_instrument_type` на `(instrument_type)` — сценарий 1: «показать все акции TQBR».
- `idx_moex_instruments_asset_code` на `(asset_code)` где `asset_code IS NOT NULL` — сценарий 2: «все фьючерсы на один базовый актив (Si)».

**Особенности**: вызов 1a и 1b идут в одну таблицу. Акции и фьючерсы различаются полем `instrument_type`. Оба вызова публичные (ISS), не требуют ключа APIM.

---

### Шаг 2. moex_stock_details

| | |
|---|---|
| Миграция | V002__create_moex_stock_details.sql |
| Зависит от | `moex_instruments` (FK: secid) |
| Кто зависит | никто напрямую |
| Режим доступа | публичный ISS (базовые поля) |

**Вызов**: тот же `GetInfoTradedStockAssets()` из шага 1a — данные берутся из того же DTO `StockSecurityDTO`, но пишутся в другую таблицу.

Маппинг полей:

| DTO-поле | → Колонка таблицы | Тип | Преобразование |
|---|---|---|---|
| `SECID` | `secid` (PK, FK) | text | as-is |
| `BOARDID` | `boardid` | text | as-is |
| `MARKETCODE` | `marketcode` | text | as-is |
| `LOTSIZE` | `lotsize` | int | as-is |
| `FACEVALUE` | `facevalue` | numeric | as-is |
| `PREVLEGALCLOSEPRICE` | `prev_close_price` | numeric | as-is |
| `PREVDATE` | `prev_date` | date | parse yyyy-MM-dd |
| — | `status` | text | NULL (обогащение позже) |
| — | `decimals` | int | NULL (обогащение позже) |
| — | `minstep` | numeric | NULL (обогащение позже) |
| — | `isin` | text | NULL (обогащение позже) |
| — | `currency_id` | text | NULL (обогащение позже) |
| — | `list_level` | int | NULL (обогащение позже) |
| — | `issue_size` | bigint | NULL (обогащение позже) |
| — | `settle_date` | date | NULL (обогащение позже) |
| — | `updated_at` | timestamptz | `now()` |

**Upsert**: `INSERT ... ON CONFLICT (secid) DO UPDATE SET boardid, lotsize, facevalue, prev_close_price, prev_date, updated_at`. Поля MarketStatistics не затрагиваются базовым upsert-ом.

**Структурные индексы этой миграции**:
- PK `secid` — сценарий 5: «карточка акции». Он же обслуживает FK → `moex_instruments.secid`.

**Сценарные индексы (отдельной миграцией позже)**:
- `idx_moex_stock_details_list_level` на `(list_level)` — сценарий 1: «отсортировать по уровню листинга».

---

### Шаг 3. moex_futures_details

| | |
|---|---|
| Миграция | V003__create_moex_futures_details.sql |
| Зависит от | `moex_instruments` (FK: secid) |
| Кто зависит | никто напрямую; используется в модели издержек |
| Режим доступа | публичный ISS (базовые поля) |

**Вызов**: тот же `GetInfoTradedFuturesAssets()` из шага 1b — данные берутся из `FuturesSecurityDTO`, пишутся в другую таблицу.

Маппинг полей:

| DTO-поле | → Колонка таблицы | Тип | Преобразование |
|---|---|---|---|
| `SECID` | `secid` (PK, FK) | text | as-is |
| — | `boardid` | text, not null | константа `'RFUD'` |
| `INITIALMARGIN` | `initial_margin` | numeric | as-is |
| `PREVSETTLEPRICE` | `prev_settle_price` | numeric | as-is |
| `PREVPRICE` | `prev_price` | numeric | as-is |
| `MINSTEP` | `minstep` | numeric | as-is |
| `STEPPRICE` | `stepprice` | numeric | as-is |
| `LOTVOLUME` | `lotvolume` | int | as-is |
| `DECIMALS` | `decimals` | int | as-is |
| `LASTTRADEDATE` | `last_trade_date` | date | parse yyyy-MM-dd |
| `LASTDELDATE` | `last_del_date` | date | parse yyyy-MM-dd |
| `PREVOPENPOSITION` | `prev_open_position` | bigint | as-is |
| `HIGHLIMIT` | `high_limit` | numeric | as-is |
| `LOWLIMIT` | `low_limit` | numeric | as-is |
| — | `buysell_fee` | numeric | NULL (обогащение позже) |
| — | `scalper_fee` | numeric | NULL (обогащение позже) |
| — | `last_settle_price` | numeric | NULL (обогащение позже) |
| — | `settle_price_clr` | numeric | NULL (обогащение позже) |
| — | `im_time` | timestamp | NULL (обогащение позже) |
| — | `updated_at` | timestamptz | `now()` |

**Upsert**: `INSERT ... ON CONFLICT (secid) DO UPDATE SET initial_margin, minstep, stepprice, ..., updated_at`. Поля MarketStatistics не затрагиваются.

**Структурные индексы этой миграции**:
- PK `secid` — сценарий 6: «карточка фьючерса». Он же обслуживает FK → `moex_instruments.secid`.

**Сценарные индексы (отдельной миграцией позже)**:
- `idx_moex_futures_details_last_trade_date` на `(last_trade_date)` — сценарий 4: «до экспирации N дней», сценарий 50: «контракты экспирируются в ближайшие 30 дней».

**Важно для модели издержек**: `buysell_fee` и `scalper_fee` критичны (Первая вертикаль: «решение без учёта комиссии запрещено»). Заполняются на шаге обогащения (волна 4).

---

### Шаг 4. moex_forts_contracts

| | |
|---|---|
| Миграция | V004__create_moex_forts_contracts.sql |
| Зависит от | ничего (PK = secid, FK на moex_instruments не ставится: таблица содержит и неторгуемые контракты) |
| Кто зависит | никто напрямую |
| Режим доступа | платный APIM (нужен AlgKey) |

**Вызов**:

| | |
|---|---|
| Клиент | `MoexHttpCalendarClient` |
| Метод | `GetFuturesSecuritiesAll()` |
| Endpoint | `GET /calendars/futures/securities.json` |
| DTO | `List<CalendarFortsContractDTO>` (первый проход) |
| Парсер | `ParsingCalendarUtf8.ParseFuturesSecurities` — два прохода по одним байтам |

Маппинг полей:

| DTO-поле | → Колонка таблицы | Тип |
|---|---|---|
| `SecId` | `secid` (PK) | text |
| `AssetCode` | `asset_code` | text |
| `ShortName` | `shortname` | text |
| `ExecType` | `exec_type` | text |
| `ContractName` | `contract_name` | text |
| `ExpirationDate` | `expiration_date` | date |
| `EndDate` | `end_date` | date |
| `ExpirationType` | `expiration_type` | text |
| `ExpirationTime` | `expiration_time` | time |
| `WeekendSession` | `weekend_session` | int |
| — | `updated_at` | timestamptz |

**Upsert**: `INSERT ... ON CONFLICT (secid) DO UPDATE SET ...`.

**Структурные индексы этой миграции**:
- PK `secid` — уникальность контракта.

**Сценарные индексы (отдельной миграцией позже)**:
- `idx_moex_forts_contracts_asset_code_expiration_date` на `(asset_code, expiration_date)` — сценарий 2: «все контракты на Si с датами экспирации», сценарий 3: «ближний контракт», сценарий 49: «таблица ролла», сценарий 50: «экспирация в ближайшие 30 дней».

**Особенность**: метод `GetFuturesSecuritiesAll` возвращает и контракты, и опционные серии за один вызов (два прохода парсера). Данные контрактов записываются здесь, опционные серии — на шаге 5.

---

### Шаг 5. moex_options_series

| | |
|---|---|
| Миграция | V005__create_moex_options_series.sql |
| Зависит от | ничего |
| Кто зависит | никто |
| Режим доступа | платный APIM |

**Вызов**: тот же `GetFuturesSecuritiesAll()` из шага 4 — второй проход парсера.

| | |
|---|---|
| DTO | `List<CalendarOptionsSeriesDTO>` (второй проход) |

Маппинг полей:

| DTO-поле | → Колонка таблицы | Тип |
|---|---|---|
| `SeriesName` | `series_name` (PK) | text |
| `AssetTypeName` | `asset_type_name` | text |
| `AssetCode` | `asset_code` | text |
| `SeriesType` | `series_type` | text |
| `ExecType` | `exec_type` | text |
| `MarginStyle` | `margin_style` | text |
| `ContractName` | `contract_name` | text |
| `ExpirationDate` | `expiration_date` | date |
| `ExpirationType` | `expiration_type` | text |
| `ExpirationTime` | `expiration_time` | time |
| `WeekendSession` | `weekend_session` | int |
| — | `updated_at` | timestamptz |

**Upsert**: `INSERT ... ON CONFLICT (series_name) DO UPDATE SET ...`.

**Индексы из сценариев**:
- PK `series_name`.
- `idx_moex_options_series_asset_code` на `(asset_code)` — сценарий 52: «опционные серии по базовому активу».

**Экономия вызовов**: шаги 4 и 5 — один HTTP-запрос, два прохода парсера, две таблицы. Один вызов = два результата.

---

## 3. Волна 2 — Календарь и расписание

### Шаг 6. moex_calendar_days

| | |
|---|---|
| Миграция | V006__create_moex_calendar_days.sql |
| Зависит от | ничего |
| Кто зависит | используется в сценарии 38 (поиск дыр) совместно с loaded_ranges |
| Режим доступа | платный APIM |

**Вызов 6a: фондовый рынок**

| | |
|---|---|
| Клиент | `MoexHttpCalendarClient` |
| Метод | `GetStockOffDays()` |
| Endpoint | `GET /calendars/stock.json` |
| DTO | `List<CalendarOffDaysMarketDTO>` |
| Парсер | `ParsingCalendarUtf8.ParseOffDaysMarket` |

**Вызов 6b: срочный рынок**

| | |
|---|---|
| Метод | `GetFuturesOffDays()` |
| Endpoint | `GET /calendars/futures.json` |

Оба вызова пишут в одну таблицу, отличаются полем `market` (`'stock'` или `'futures'`).

Маппинг полей:

| DTO-поле | → Колонка таблицы | Тип |
|---|---|---|
| `TradeDate` | `trade_date` (PK) | date |
| — | `market` (PK) | text, `'stock'` или `'futures'` |
| `IsTraded` | `is_traded` | int |
| `TradeSessionDate` | `trade_session_date` | date, nullable |
| `Reason` | `reason` | text |
| `UpdateTime` | `moex_update_time` | timestamp |
| — | `updated_at` | timestamptz |

**Upsert**: `INSERT ... ON CONFLICT (trade_date, market) DO UPDATE SET ...`.

**Индексы из сценариев**:
- Составной PK `(trade_date, market)` — сценарий 13: «сегодня торговый день?» (`WHERE trade_date = CURRENT_DATE AND market = 'stock'`).
- `idx_moex_calendar_days_reason` на `(market, reason)` — сценарий 14: «ближайшие нерабочие дни: праздники отдельно, выходные с weekend-сессией отдельно».

---

### Шаг 7. moex_trading_sessions

| | |
|---|---|
| Миграция | V007__create_moex_trading_sessions.sql |
| Зависит от | ничего (secid nullable — не FK) |
| Кто зависит | никто |
| Режим доступа | платный APIM |

**Вызов 7a: акции**

| | |
|---|---|
| Клиент | `MoexHttpCalendarClient` |
| Метод | `GetStockSessionWithTypes()` |
| Endpoint | `GET /calendars/stock/session.json` |
| DTO | `(List<CalendarStockSessionDTO>, List<CalendarSessionTypeDTO>)` |
| Парсер | `ParsingCalendarUtf8.ParseStockSession` — два прохода |

**Вызов 7b: фьючерсы**

| | |
|---|---|
| Метод | `GetFuturesSessionWithTypes()` |
| Endpoint | `GET /calendars/futures/session.json` |
| DTO | `(List<CalendarFuturesSessionDTO>, List<CalendarSessionTypeDTO>)` |
| Парсер | `ParsingCalendarUtf8.ParseFuturesSession` — два прохода |

Маппинг полей в `moex_trading_sessions`:

| DTO-поле stock | DTO-поле futures | → Колонка | Преобразование |
|---|---|---|---|
| — | — | `id` (PK, IDENTITY) | автогенерация |
| — | — | `market` | `'stock'` или `'futures'` |
| `TradeDate` | `TradeSessionDate` | `session_date` | date |
| `TradingSession` | — (null) | `trading_session` | int, nullable |
| `BoardId` | `BoardId` | `boardid` | text |
| `SecId` | `SecId` | `secid` | пустой/"-" → NULL |
| `Type` | `Type` | `session_type` | text |
| TradeDate + TimeFrom | `TimeFrom` | `time_from` | timestamp (stock: date + time → timestamp) |
| TradeDate + TimeTill | `TimeTill` | `time_till` | timestamp, nullable |
| `UpdateTime` | `UpdateTime` | `moex_update_time` | timestamp |

**Дедупликация**: `UNIQUE NULLS NOT DISTINCT (market, session_date, boardid, secid, session_type, time_from)`.

**Upsert**: `INSERT ... ON CONFLICT ... DO UPDATE SET time_till, trading_session, moex_update_time`.

**Индексы из сценариев**:
- `idx_moex_trading_sessions_date` на `(market, session_date)` — сценарий 16: «расписание сессий на сегодня».
- `idx_moex_trading_sessions_secid` на `(secid)` где `secid IS NOT NULL` — сценарий 17: «индивидуальное расписание инструмента».

**Важная нормализация**: stock-сессия `TradeDate = '2026-05-24'`, `TimeFrom = '10:00:00'` → `time_from = '2026-05-24 10:00:00'`. Пустой `SecId` и `"-"` нормализуются в NULL при записи.

**Экономия вызовов**: вызовы 7a и 7b возвращают и сессии, и типы сессий. Типы идут в шаг 8.

---

### Шаг 8. moex_session_types

| | |
|---|---|
| Миграция | V008__create_moex_session_types.sql |
| Зависит от | ничего |
| Кто зависит | никто (справочная расшифровка) |
| Режим доступа | платный APIM |

**Вызов**: тот же `GetStockSessionWithTypes()` / `GetFuturesSessionWithTypes()` из шага 7 — второй проход парсера.

| DTO-поле | → Колонка | Тип |
|---|---|---|
| `Type` | `type_code` (PK) | text |
| — | `market` (PK) | text |
| `Title` | `title` | text |

**Upsert**: `INSERT ... ON CONFLICT (type_code, market) DO UPDATE SET title`.

**Индексы**: только составной PK. Справочник ~10-20 записей.

---

## 4. Волна 3 — Ограничения и изменения

### Шаг 9. moex_suspension_reasons

| | |
|---|---|
| Миграция | V009__create_moex_suspension_reasons.sql |
| Зависит от | ничего |
| Кто зависит | `moex_suspensions` (FK: reason_id) |
| Режим доступа | платный APIM |

**Вызов**:

| | |
|---|---|
| Клиент | `MoexHttpCalendarClient` |
| Метод | `GetSuspendedReasons()` |
| Endpoint | `GET /calendars/stock/securities/suspended/details.json` (блок suspended.reasons) |
| DTO | `List<CalendarSuspendedReasonDTO>` |
| Парсер | `ParsingCalendarUtf8.ParseSuspendedReasons` |

| DTO-поле | → Колонка | Тип |
|---|---|---|
| `Id`?.ToString() | `reason_id` (PK) | text |
| `Title` | `title` | text |

**Upsert**: `INSERT ... ON CONFLICT (reason_id) DO UPDATE SET title`.

**Важно**: `reason_id` хранится как text, хотя значения числовые. MOEX metadata определяет тип как string. Загружается ДО `moex_suspensions`, потому что suspensions ссылаются на reasons.

---

### Шаг 10. moex_suspensions

| | |
|---|---|
| Миграция | V010__create_moex_suspensions.sql |
| Зависит от | логическая связь по `reason_id` с `moex_suspension_reasons`, FK не ставится. Логическая связь по `secid` с `moex_instruments`, FK не ставится |
| Кто зависит | никто |
| Режим доступа | платный APIM |

**Вызов**:

| | |
|---|---|
| Клиент | `MoexHttpCalendarClient` |
| Метод | `GetSuspended()` |
| Endpoint | `GET /calendars/stock/securities/suspended/details.json` (блок suspended + suspended.cursor) |
| DTO | `List<CalendarSuspendedDTO>` |
| Парсер | `ParsingCalendarUtf8.ParseSuspended` |
| Пагинация | cursor-пагинация, до 160k+ записей |

Маппинг полей:

| DTO-поле | → Колонка | Тип |
|---|---|---|
| — | `id` (PK, IDENTITY) | bigint |
| `SecId` | `secid` | text |
| `ReasonId` | `reason_id` | text |
| `DateFrom` | `date_from` | date |
| `DateTill` | `date_till` | date, nullable |
| `BoardId` | `boardid` | text, nullable |
| `SettleCodes` | `settle_codes` | text, nullable |
| `ChangeDate` | `change_date` | date |
| `UpdateTime` | `moex_update_time` | timestamp |

**Дедупликация**: `UNIQUE NULLS NOT DISTINCT (secid, reason_id, date_from, date_till, boardid, settle_codes)`.

**Структурные индексы этой миграции**:
- PK `(id)`.
- `UNIQUE NULLS NOT DISTINCT (secid, reason_id, date_from, date_till, boardid, settle_codes)`.

**Сценарные индексы (отдельной миграцией позже)**:
- `idx_moex_suspensions_secid_date_from` на `(secid, date_from)` — сценарий 20: «активные приостановки», сценарий 21: «приостановка на конкретную дату», сценарий 22: «инструмент не приостановлен за период».
- `idx_moex_suspensions_date_till` на `(date_till)` — сценарий 25: «сколько приостановлено прямо сейчас».

**Предупреждение по объёму**: до 160k+ записей, cursor-пагинация обязательна. Защитный предел `MaxPagesPerLoad = 10000` из MoexOptions.

---

### Шаг 11. moex_security_attributes

| | |
|---|---|
| Миграция | V011__create_moex_security_attributes.sql |
| Зависит от | ничего |
| Кто зависит | `moex_security_changes` (логическая связь по attribute_name) |
| Режим доступа | платный APIM |

**Вызов**:

| | |
|---|---|
| Клиент | `MoexHttpCalendarClient` |
| Метод | `GetSecurityAttributes()` |
| Endpoint | `GET /calendars/stock/securities/changes.json` (блок securities.attributes) |
| DTO | `List<CalendarSecurityAttributeDTO>` |
| Парсер | `ParsingCalendarUtf8.ParseSecurityChangesWithAttributes` (возвращает и attributes, и changes) |

| DTO-поле | → Колонка | Тип |
|---|---|---|
| `Name` | `name` (PK) | text |
| `Type` | `data_type` | text |
| `Title` | `title` | text |

**Upsert**: `INSERT ... ON CONFLICT (name) DO UPDATE SET data_type, title`.

Справочник: ~23 записи. Загружается ДО `moex_security_changes`.

---

### Шаг 12. moex_security_changes

| | |
|---|---|
| Миграция | V012__create_moex_security_changes.sql |
| Зависит от | `moex_security_attributes` (логическая), `moex_instruments` (secid — логическая) |
| Кто зависит | никто |
| Режим доступа | платный APIM |

**Вызов**: тот же `GetSecurityChanges()` / `GetSecurityAttributes()` — данные changes идут из того же endpoint.

| | |
|---|---|
| Метод | `GetSecurityChanges()` |
| DTO | `List<CalendarSecurityChangeDTO>` |
| Пагинация | cursor-пагинация |

Маппинг полей:

| DTO-поле | → Колонка | Тип |
|---|---|---|
| — | `id` (PK, IDENTITY) | bigint |
| `UpdateTime` | `moex_update_time` | timestamp |
| `Action` | `action` | text |
| `SecId` | `secid` | text |
| `AttributeName` | `attribute_name` | text |
| `BeforeValue` | `before_value` | text, nullable |
| `AfterValue` | `after_value` | text, nullable |

**Дедупликация**: `UNIQUE NULLS NOT DISTINCT (moex_update_time, action, secid, attribute_name, before_value, after_value)`.

**Индексы из сценариев**:
- `idx_moex_security_changes_time` на `(moex_update_time)` — сценарий 26: «изменения за последние сутки».
- `idx_moex_security_changes_secid` на `(secid)` — сценарий 11: «инструменты, у которых сменился атрибут», сценарий 28: «инструмент изменил атрибуты».
- `idx_moex_security_changes_attr` на `(attribute_name)` — сценарий 27: «фильтр по типу атрибута».

---

## 5. Волна 4 — Обогащение из MarketStatistics

Эта волна не создаёт новых таблиц. Она обогащает `moex_stock_details` и `moex_futures_details`, созданные в волне 1. Требует, чтобы `moex_instruments` уже были заполнены — цикл идёт по списку инструментов из базы.

### Шаг 13a. Обогащение moex_stock_details

| | |
|---|---|
| Миграция | нет (таблица уже создана в V002) |
| Зависит от | `moex_instruments` заполнена акциями |
| Режим доступа | платный APIM |

**Вызов** (per-ticker, цикл):

| | |
|---|---|
| Клиент | `MoexRealtimeRestClient` |
| Метод | `GetMarketStatisticsStockSecuritiesAsync(ticker)` |
| Endpoint | `GET /engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json?iss.only=securities` |
| DTO | `MarketStatisticsStockSecuritiesDTO?` |
| Парсер | `ParsingMarketStatisticsUtf8.ParseStockSecurities` |
| Схема | 10 из 27 колонок: SECID[0], BOARDID[1], STATUS[6], DECIMALS[8], MINSTEP[14], ISSUESIZE[18], ISIN[19], CURRENCYID[23], LISTLEVEL[25], SETTLEDATE[26] |

**UPDATE** (не INSERT — строка уже есть):

```sql
UPDATE moex_stock_details
SET status = $1, decimals = $2, minstep = $3,
    isin = $4, currency_id = $5, list_level = $6,
    issue_size = $7, settle_date = $8, updated_at = now()
WHERE secid = $9 AND boardid = $10;
```

**Преобразования**: `SETTLEDATE` (string `yyyy-MM-dd`) → `date`. Если не распарсилось — `NULL`, событие в логах.

**Особенность per-ticker**: один HTTP-запрос на один инструмент. Для N акций = N запросов. Rate limiter: 8 запросов/сек (MoexOptions.MaxRequestsPerSecond). Для ~250 акций TQBR ≈ 31 секунда.

---

### Шаг 13b. Обогащение moex_futures_details

| | |
|---|---|
| Миграция | нет (таблица уже создана в V003) |
| Зависит от | `moex_instruments` заполнена фьючерсами |
| Режим доступа | платный APIM |

**Вызов** (per-ticker, цикл):

| | |
|---|---|
| Клиент | `MoexRealtimeRestClient` |
| Метод | `GetMarketStatisticsFuturesSecuritiesAsync(ticker)` |
| Endpoint | `GET /engines/futures/markets/forts/boards/RFUD/securities/{ticker}.json?iss.only=securities` |
| DTO | `MarketStatisticsFuturesSecuritiesDTO?` |
| Парсер | `ParsingMarketStatisticsUtf8.ParseFuturesSecurities` |
| Схема | 7 из 26 колонок: SECID[0], BOARDID[1], LASTSETTLEPRICE[18], IMTIME[20], BUYSELLFEE[21], SCALPERFEE[22], SETTLEPRICE_CLR[25] |

**UPDATE**:

```sql
UPDATE moex_futures_details
SET buysell_fee = $1, scalper_fee = $2,
    last_settle_price = $3, settle_price_clr = $4,
    im_time = $5, updated_at = now()
WHERE secid = $6
  AND boardid = $7;
```

**Преобразования**: `IMTIME` (string `yyyy-MM-dd HH:mm:ss`) → `timestamp without time zone`. Если не распарсилось — `NULL`, событие в логах.

**Сверка boardid**: `BOARDID` берётся из `MarketStatisticsFuturesSecuritiesDTO.BOARDID` и сверяется с ожидаемым `'RFUD'`. Если `BOARDID` из MarketStatistics не совпадает с ожидаемым, это не должно быть тихим no-op. Пишется событие в лог с secid, ожидаемым boardid и фактически полученным boardid. Инструмент не считается успешно обогащённым.

**Критичность для модели издержек**: `buysell_fee` и `scalper_fee` — биржевая комиссия. Без них модель издержек неполна (Первая вертикаль).

---

## 6. Волна 5 — Связи, загрузки, издержки

### Шаг 14. moex_instrument_relations

| | |
|---|---|
| Миграция | V013__create_moex_instrument_relations.sql |
| Зависит от | `moex_instruments` заполнена (нужны оба конца связи) |
| Кто зависит | никто |
| Источник | автоматически из `asset_code` + вручную оператором |

**Нет HTTP-вызова MOEX.** Данные формируются автоматически:

Для связей фьючерс → конкретная акция (когда asset_code совпадает с secid акции):

```sql
INSERT INTO moex_instrument_relations (source_secid, target_secid, target_asset_code, relation_type, confidence)
SELECT f.secid, s.secid, f.asset_code, 'future_underlying', 'auto'
FROM moex_instruments f
JOIN moex_instruments s ON s.secid = f.asset_code AND s.instrument_type = 'stock'
WHERE f.instrument_type = 'futures' AND f.asset_code IS NOT NULL
ON CONFLICT (source_secid, target_secid, target_asset_code, relation_type) DO NOTHING;
```

Для фьючерсов, чей `asset_code` не совпал ни с одной акцией:

```sql
INSERT INTO moex_instrument_relations (source_secid, target_asset_code, relation_type, confidence)
SELECT i.secid, i.asset_code, 'future_underlying', 'auto'
FROM moex_instruments i
WHERE i.instrument_type = 'futures' AND i.asset_code IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM moex_instruments s
    WHERE s.secid = i.asset_code AND s.instrument_type = 'stock'
  )
ON CONFLICT (source_secid, target_secid, target_asset_code, relation_type) DO NOTHING;
```

Если `asset_code` фьючерса совпал с `secid` акции, создаётся только точная строка связи. Обобщённая строка `target_secid = NULL` для такого фьючерса не создаётся.

**Индексы из сценариев**:
- `idx_moex_relations_source` на `(source_secid)` — сценарий 9: «по фьючерсу → базовый актив».
- `idx_moex_relations_target` на `(target_secid)` — сценарий 8: «по SBER → какие фьючерсы».
- `idx_moex_relations_asset_code` на `(target_asset_code)` — поиск по asset_code.

---

### Шаг 15. moex_load_tasks

| | |
|---|---|
| Миграция | V014__create_moex_load_tasks.sql |
| Зависит от | `moex_instruments` (FK: secid) |
| Кто зависит | `moex_loaded_ranges` (FK: last_task_id) |
| Источник | оператор (создание задания) |

**Нет HTTP-вызова MOEX.** Задания создаются оператором.

**PostgreSQL 18 фича**: `id uuid PRIMARY KEY DEFAULT uuidv7()` — хронологический UUID, генерируется базой.

**Индексы из сценариев**:
- PK `id` (uuid) — B-tree без фрагментации благодаря UUIDv7.
- `idx_moex_load_tasks_status` на `(status)` — сценарий 33: «все pending», сценарий 34: «все error».
- `idx_moex_load_tasks_secid` на `(secid)` — сценарий 36: «задания за сегодня по инструменту».
- `idx_moex_load_tasks_created` на `(created_at)` — сценарий 36: «задания за сегодня».

---

### Шаг 16. moex_loaded_ranges

| | |
|---|---|
| Миграция | V015__create_moex_loaded_ranges.sql |
| Зависит от | `moex_load_tasks` (FK: last_task_id) |
| Кто зависит | никто |
| Источник | результат успешной загрузки |

**Нет HTTP-вызова MOEX.** Заполняется автоматически после успешного завершения load_task.

**Дедупликация**: `UNIQUE NULLS NOT DISTINCT (secid, market, boardid, data_kind, interval, date_from, date_till, storage_target)`.

**Индексы из сценариев**:
- `idx_moex_loaded_ranges_secid_kind` на `(secid, data_kind)` — сценарий 37: «загруженные диапазоны по инструменту», сценарий 38: «поиск дыр».
- `idx_moex_loaded_ranges_last_success` на `(last_success_at)` — сценарий 39: «устаревшая загрузка».

---

### Шаг 17. moex_broker_tariffs

| | |
|---|---|
| Миграция | V016__create_moex_broker_tariffs.sql |
| Зависит от | ничего |
| Кто зависит | никто |
| Источник | оператор, ручной ввод |

**Нет HTTP-вызова MOEX.** Оператор вводит вручную.

**Индексы из сценариев**:
- `idx_moex_broker_tariffs_market_valid` на `(market, valid_from)` — сценарий 43: «полная комиссия за сделку» (текущий тариф: `WHERE market = 'futures' AND valid_from <= CURRENT_DATE AND (valid_till IS NULL OR valid_till >= CURRENT_DATE)`).
- `idx_moex_broker_tariffs_broker` на `(broker_name, tariff_name)` — сценарий 45: «история тарифов брокера».

---

## 7. Сводная таблица: миграция → таблица → метод → клиент → режим

| V# | Таблица | Метод клиента | Клиент | Режим | HTTP-запросов | Примечание |
|---|---|---|---|---|---|---|
| 001 | moex_instruments | GetInfoTradedStockAssets + GetInfoTradedFuturesAssets | MoexHttpIssClient | ISS публичный | 2 | два вызова → одна таблица |
| 002 | moex_stock_details | тот же GetInfoTradedStockAssets | MoexHttpIssClient | ISS публичный | 0 (данные из шага 1a) | тот же DTO, другая таблица |
| 003 | moex_futures_details | тот же GetInfoTradedFuturesAssets | MoexHttpIssClient | ISS публичный | 0 (данные из шага 1b) | тот же DTO, другая таблица |
| 004 | moex_forts_contracts | GetFuturesSecuritiesAll | MoexHttpCalendarClient | APIM платный | 1 | два прохода → две таблицы |
| 005 | moex_options_series | тот же GetFuturesSecuritiesAll | MoexHttpCalendarClient | APIM платный | 0 (данные из шага 4) | второй проход |
| 006 | moex_calendar_days | GetStockOffDays + GetFuturesOffDays | MoexHttpCalendarClient | APIM платный | 2 | два вызова → одна таблица |
| 007 | moex_trading_sessions | GetStockSessionWithTypes + GetFuturesSessionWithTypes | MoexHttpCalendarClient | APIM платный | 2 | два прохода каждый |
| 008 | moex_session_types | те же два метода | MoexHttpCalendarClient | APIM платный | 0 (данные из шага 7) | второй проход |
| 009 | moex_suspension_reasons | GetSuspendedReasons | MoexHttpCalendarClient | APIM платный | 1 | 28 записей |
| 010 | moex_suspensions | GetSuspended | MoexHttpCalendarClient | APIM платный | 1+ (cursor) | до 160k+ записей |
| 011 | moex_security_attributes | GetSecurityAttributes | MoexHttpCalendarClient | APIM платный | 1 | 23 записи |
| 012 | moex_security_changes | GetSecurityChanges | MoexHttpCalendarClient | APIM платный | 1+ (cursor) | cursor-пагинация |
| — | moex_stock_details (UPDATE) | GetMarketStatisticsStockSecuritiesAsync | MoexRealtimeRestClient | APIM платный | N (per-ticker) | ~250 акций ≈ 31 сек |
| — | moex_futures_details (UPDATE) | GetMarketStatisticsFuturesSecuritiesAsync | MoexRealtimeRestClient | APIM платный | M (per-ticker) | ~100 фьючерсов ≈ 13 сек |
| 013 | moex_instrument_relations | нет вызова MOEX | — | — | 0 | авто из asset_code |
| 014 | moex_load_tasks | нет вызова MOEX | — | — | 0 | оператор |
| 015 | moex_loaded_ranges | нет вызова MOEX | — | — | 0 | результат загрузки |
| 016 | moex_broker_tariffs | нет вызова MOEX | — | — | 0 | оператор вручную |

**Итого HTTP-запросов для полного заполнения**: 2 (ISS) + 8 (Calendar) + N+M (MarketStatistics) ≈ 10 + 350 ≈ 360 запросов. При 8 запросах/сек ≈ 45 секунд. Cursor-пагинация suspensions и changes добавит ещё, зависит от объёма.

---

## 8. Связь с другими документами

- **MOEX_Management_Model_v0_1.md** — описание 16 таблиц, поля, типы, upsert-правила.
- **Контракт источника MOEX** — описание клиентов, методов, endpoint-ов, DTO, парсеров.
- **Обзор системы** — раздел 5.1 (PostgreSQL — управляющая база), раздел 5.3 (подключения по необходимости).
- **Правила разработки** — правило 10 (без универсальных хранилищ), правило 11 (стандартное логирование).
