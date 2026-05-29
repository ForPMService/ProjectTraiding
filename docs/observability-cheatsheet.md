# Шпаргалка по стеку наблюдаемости — ProjectTraiding

Версия 0.1. Шпаргалка для повседневной работы со стеком наблюдаемости. Все примеры привязаны к конкретным метрикам, логам и трейсам репозитория ProjectTraiding.

---

## 1. Зачем стек и что он делает

Стек наблюдаемости позволяет видеть, что происходит с работающим приложением, не заглядывая в код и не подключая отладчик. Вместо «приложение упало, не понимаю почему» — три потока данных, каждый из которых отвечает на свой вопрос.

**Логи** — текстовые записи событий: «что именно произошло в этот момент». Пример: «MOEX HTTP response received, status=200, elapsed_ms=47». Детальные, дискретные, дорогие в больших объёмах. Хранятся в **Loki**.

**Метрики** — числа во времени: «сколько и как быстро». Пример: «за последние 5 минут было 42 HTTP-запроса к MOEX, средняя латентность 87 мс». Дёшевы, агрегируемы, но без подробностей отдельных запросов. Хранятся в **Prometheus**.

**Трейсы** — дерево операций одного запроса: «как конкретно этот запрос прошёл через систему и где провёл время». Пример: span `moex.load` → внутри span HTTP к бирже → внутри span пагинации. Показывают причинно-следственную цепочку. Хранятся в **Tempo**.

**Grafana** — единый интерфейс поверх всех трёх хранилищ. Главная ценность — переходы: из метрики в логи, из лога в трейс. Один инцидент смотришь со всех трёх сторон, не переключая инструменты.

---

## 2. Архитектура на стенде

```
ProjectTraiding.Api (:5025)
       │
       │  OTLP (gRPC, порт 4317)
       │  (все три потока одним протоколом)
       ▼
OpenTelemetry Collector (:13133 health)
       │
       ├── логи ────► Loki (:3100)          ──┐
       ├── метрики ─► Prometheus (:9090)    ──┼──► Grafana (:3000)
       └── трейсы ──► Tempo (:3200)         ──┘
```

**Приложение** не знает про Loki, Prometheus, Tempo. Оно отправляет всё в одну точку — Collector — одним протоколом (OTLP). Формат данных стандартный (OpenTelemetry), не привязан к конкретным хранилищам.

**Collector** — роутер: принимает OTLP и раскладывает по хранилищам. Его конфигурация (`infrastructure/otel-collector/config.yaml`) определяет, куда что попадает. Приложение при этом не трогается.

**Prometheus** работает иначе, чем Loki и Tempo: он сам ходит за данными (scrape), а не принимает их. Collector открывает endpoint `:8889`, Prometheus каждые 15 секунд его опрашивает. Это нормально и так задумано — модель pull, а не push.

---

## 3. Основные понятия

### Общие

**OTLP (OpenTelemetry Protocol)** — формат, которым приложение отдаёт телеметрию. Один протокол для логов, метрик и трейсов. Приложение вызывает `AddOtlpExporter()`, и все три потока уходят на `OTEL_EXPORTER_OTLP_ENDPOINT` (у нас `http://localhost:4317`).

**Resource** — набор атрибутов, общий для всей телеметрии: `service.name=ProjectTraiding.Api`. По этому имени фильтруешь «свои» данные в хранилищах.

**Structured metadata** — поля, прикреплённые к записи, но не вклеенные в текст. `trace_id`, `span_id`, `level` — это structured metadata. Они не видны в «теле» строки, но доступны как отдельные поля для фильтрации. Это важно при настройке переходов (derived fields).

### Логи

**EventId** — числовой код события. У нас зафиксированы диапазоны: 100–151 (MOEX), 160–169 (raw-capture). По EventId можно однозначно понять, что произошло, не читая текст.

**Level (уровень)** — серьёзность записи:
- **Debug** — частые технические детали, обычно выключены в production. Пример: каждый HTTP-запрос к MOEX (101), permit rate limiter (150).
- **Information** — значимое штатное событие. Пример: загрузка началась (100), страница данных получена (120).
- **Warning** — система продолжила работу, но есть риск. Пример: retry (110), safety cap сработал (141).
- **Error** — операция не выполнена. Пример: HTTP-ошибка (130), ошибка парсинга (131).

**Source-generated logging** — способ, которым .NET записывает логи. `[LoggerMessage]`-атрибуты на статических методах (`MoexLogMessages`, `RawCaptureLogMessages`). Не обёртка — штатный механизм .NET, AOT-совместимый.

### Метрики

**Counter (счётчик)** — число, которое только растёт: сколько всего запросов, сколько ошибок, сколько байт. Никогда не уменьшается. На сырой счётчик смотреть бесполезно (он просто растёт) — всегда оборачиваешь в `rate()`, чтобы увидеть скорость изменения.

**Histogram (гистограмма)** — распределение значений: латентность запросов, время ожидания. Хранит не отдельные значения, а «сколько значений попало в каждый диапазон (bucket)». Из гистограммы считаешь перцентили: p50 (медиана), p95, p99.

**Gauge (измеритель)** — текущее значение, которое может расти и падать: температура, размер очереди, потребление памяти. В нашем стеке gauge-метрики приходят от runtime-инструментации (.NET memory, GC).

**Label (метка)** — дополнительное измерение метрики. `moex_http_requests_total{source="MOEX_ISS"}` и `moex_http_requests_total{source="MOEX_ALGOPACK"}` — один счётчик, разбитый по меткам. Метки дают разрезы без умножения метрик.

**Scrape** — Prometheus сам ходит за метриками (pull-модель). Каждые 15 секунд опрашивает Collector на `:8889`. Между scrape-ами данные невидимы — поэтому `rate()` нужно давать окно не меньше 2–4 интервалов (т.е. минимум `[1m]`).

### Трейсы

**Trace (трейс)** — полная картина одной операции от начала до конца. Состоит из span-ов. Объединяет всё одним `trace_id`.

**Span** — один «отрезок» работы внутри трейса: HTTP-запрос, цикл пагинации, запись в S3. У span-а есть имя, длительность, статус, атрибуты (теги). Span-ы вложены друг в друга — образуют дерево.

**Activity** — .NET-термин для span-а. `ActivitySource.StartActivity("moex.load")` создаёт span с именем `moex.load`. OpenTelemetry SDK транслирует Activity в OTLP span автоматически.

**trace_id** — уникальный идентификатор трейса (32 hex-символа). Все логи и span-ы одной операции разделяют один `trace_id`. Это клей, который связывает три потока: увидел `trace_id` в логе Loki → кликнул → перешёл в трейс в Tempo → видишь всё дерево.

**span_id** — идентификатор конкретного span-а внутри трейса (16 hex-символов).

---

## 4. Как запускать стек

### Поднять инфраструктуру

```powershell
cd infrastructure
docker compose up -d
```

Все 7 контейнеров должны стать `running`. Проверить:

```powershell
docker compose ps
```

### Запустить приложение с телеметрией

```powershell
cd backend/src/ProjectTraiding.Api
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
$env:OTEL_SERVICE_NAME = "ProjectTraiding.Api"
dotnet run
```

Без этих переменных приложение работает, но телеметрия не уходит в Collector.

### Остановить

```powershell
# Остановить контейнеры (данные сохраняются в Docker volumes)
cd infrastructure
docker compose down

# Остановить контейнеры и удалить данные
docker compose down -v
```

### Проверить здоровье

| Компонент | URL | Что увидишь |
|---|---|---|
| Приложение | `http://localhost:5025/healthz` | `Healthy` |
| Collector | `http://localhost:13133/` | `{"status":"Server available"}` |
| Grafana | `http://localhost:3000` | Веб-интерфейс (anonymous admin) |
| Prometheus | `http://localhost:9090` | Веб-интерфейс (собственный UI) |

---

## 5. Grafana — два режима работы

### Explore (компас слева)

Ручное расследование. Выбираешь источник данных вверху (Loki / Prometheus / Tempo), пишешь запрос, смотришь результат. Используешь, когда что-то случилось и нужно покопаться.

### Dashboards (квадраты слева)

Заранее собранные панели для постоянного мониторинга. Панели обновляются автоматически. Используешь, чтобы видеть состояние системы на одном экране, не вспоминая синтаксис запросов.

Начинать всегда проще с Explore — свободная форма, можно пробовать любые запросы.

---

## 6. Loki — логи (язык LogQL)

### Принцип

Запрос состоит из двух частей: **поток** (выбор в `{}`) → **фильтры** (после `{}`). Поток обязателен, фильтры по желанию.

### Готовые запросы для ProjectTraiding

**Все логи приложения:**
```
{service_name="ProjectTraiding.Api"}
```

**Только ошибки:**
```
{service_name="ProjectTraiding.Api"} | level="Error"
```

**Только предупреждения и ошибки:**
```
{service_name="ProjectTraiding.Api"} | level=~"Warning|Error"
```

**Логи, содержащие подстроку (полнотекстовый поиск):**
```
{service_name="ProjectTraiding.Api"} |= "MOEX load started"
```

**Логи, НЕ содержащие подстроку:**
```
{service_name="ProjectTraiding.Api"} != "MOEX HTTP response"
```

**Типичные подстроки для фильтрации:**
- `"MOEX load started"` — начало загрузки
- `"MOEX HTTP response received"` — ответ от биржи
- `"MOEX retry attempt"` — повторная попытка
- `"MOEX request failed"` — ошибка запроса
- `"MOEX paging stopped"` — остановка пагинации
- `"Raw-capture succeeded"` — запись в S3
- `"Raw-capture failed"` — ошибка записи в S3

### Подсчёт событий в единицу времени (для панелей)

```
sum(count_over_time({service_name="ProjectTraiding.Api"} | level="Error" [5m]))
```

Количество ошибок за каждые 5 минут — полезно как график, не как таблица.

---

## 7. Prometheus — метрики (язык PromQL)

### Принцип

Метрика — именованный числовой ряд. Два рефлекса:
1. **Счётчики всегда через `rate()`** — иначе видишь просто растущую линию.
2. **Разбивка через `sum by (метка)`** — иначе получаешь кашу из всех комбинаций меток.

### Имена метрик ProjectTraiding в Prometheus

OTLP-имена (из кода) превращаются в Prometheus-имена автоматически: точки → подчёркивания, у Counter-ов добавляется `_total`, у Histogram-ов появляются суффиксы `_bucket`, `_count`, `_sum`.

| OTLP-имя (в коде) | Тип | Prometheus-имя |
|---|---|---|
| `moex.http.requests` | Counter | `moex_http_requests_total` |
| `moex.http.request.duration` | Histogram | `moex_http_request_duration_*` |
| `moex.http.errors` | Counter | `moex_http_errors_total` |
| `moex.http.retries` | Counter | `moex_http_retries_total` |
| `moex.ratelimit.acquired` | Counter | `moex_ratelimit_acquired_total` |
| `moex.ratelimit.wait.duration` | Histogram | `moex_ratelimit_wait_duration_*` |
| `moex.ratelimit.queued` | Counter | `moex_ratelimit_queued_total` |
| `moex.pages.total` | Counter | `moex_pages_total_total` |
| `moex.rows.total` | Counter | `moex_rows_total_total` |
| `moex.rawcapture.writes` | Counter | `moex_rawcapture_writes_total` |
| `moex.rawcapture.errors` | Counter | `moex_rawcapture_errors_total` |
| `moex.rawcapture.bytes` | Counter | `moex_rawcapture_bytes_total` |

Плюс runtime-метрики .NET (GC, память, потоки): `process_runtime_dotnet_*` и ASP.NET Core: `http_server_*`.

### Готовые запросы

**Разведка — посмотреть все наши метрики:**
```
{__name__=~"moex_.*"}
```

**Запросов к MOEX в секунду (общий rate):**
```
sum(rate(moex_http_requests_total[5m]))
```

**Запросов в секунду по источнику (ISS / Algopack / Calendar):**
```
sum by (source) (rate(moex_http_requests_total[5m]))
```

**Ошибок в секунду по типу ошибки:**
```
sum by (error_type) (rate(moex_http_errors_total[5m]))
```

**Retry в секунду:**
```
sum(rate(moex_http_retries_total[5m]))
```

**Латентность p50 (медиана):**
```
histogram_quantile(0.50, sum by (le) (rate(moex_http_request_duration_bucket[5m])))
```

**Латентность p95:**
```
histogram_quantile(0.95, sum by (le) (rate(moex_http_request_duration_bucket[5m])))
```

**Латентность p99:**
```
histogram_quantile(0.99, sum by (le) (rate(moex_http_request_duration_bucket[5m])))
```

**Загруженных строк в секунду по типу данных:**
```
sum by (data_kind) (rate(moex_rows_total_total[5m]))
```

**Загруженных страниц в секунду по источнику:**
```
sum by (source) (rate(moex_pages_total_total[5m]))
```

**Raw-capture записей в секунду:**
```
sum(rate(moex_rawcapture_writes_total[5m]))
```

**Runtime — потребление памяти приложения:**
```
process_runtime_dotnet_gc_heap_size_bytes
```

**Runtime — сборки мусора по поколениям:**
```
sum by (generation) (rate(process_runtime_dotnet_gc_collections_total[5m]))
```

**HTTP к API приложения — запросов в секунду:**
```
sum(rate(http_server_request_duration_seconds_count[5m]))
```

### Правила окна [...]

Окно `[5m]` означает «считай rate по последним 5 минутам». Минимум — 2× интервал scrape (у нас scrape каждые 15 секунд → минимум `[30s]`, на практике `[1m]`–`[5m]` стабильнее). Чем шире окно, тем более сглаженный график.

---

## 8. Tempo — трейсы (язык TraceQL)

### Принцип

Трейс — дерево span-ов. Один запрос — один трейс. Смотришь, чтобы понять, где конкретно тормозило или сломалось.

### Как искать трейсы

**По trace_id (если знаешь):**
Просто вставь 32-символьный hex в поле поиска Tempo.

**По service name:**
В Explore → Tempo → вкладка Search → Service Name = `ProjectTraiding.Api`.

**TraceQL — по имени span-а:**
```
{ name = "moex.load" }
```

**TraceQL — по атрибуту:**
```
{ span.source = "MOEX_ALGOPACK" }
```

**TraceQL — по длительности (медленные запросы):**
```
{ name = "moex.load" && duration > 5s }
```

### Наши span-ы (Activity)

| Имя span-а | Где создаётся | Что показывает |
|---|---|---|
| `moex.load` | В начале каждого клиентского метода | Полная операция загрузки данных |
| `moex.rawcapture` | `MoexRawCaptureWriter` | Запись сырого ответа в S3 |
| HTTP span-ы | Автоинструментация `HttpClient` | Каждый HTTP-запрос к бирже |
| ASP.NET span-ы | Автоинструментация `AspNetCore` | Входящий запрос к API |

### Чтение «водопада»

Кликнув на трейс, видишь «водопад» (waterfall) — горизонтальные полоски, вложенные друг в друга:

```
[──────────── ASP.NET: GET /GetCandlesAsset ────────────]
  [─────── moex.load (source=ALGOPACK) ───────]
    [── HTTP GET iss.moex.com ──]
    [── HTTP GET iss.moex.com ──]
    [── HTTP GET iss.moex.com ──]
    [─ moex.rawcapture ─]
```

Длина полоски — время. Вложенность — причинно-следственная связь. Паузы между полосками — время, потраченное вне дочерних span-ов (наш код, rate limiter, GC).

---

## 9. Переходы между потоками

Это главная ценность стека. Типичный сценарий расследования:

### Метрика → Лог → Трейс

1. На дашборде / в Prometheus видишь **всплеск ошибок** (`moex_http_errors_total` пошёл вверх).
   - Метрика говорит «что-то не так», но не «что именно».

2. Переходишь в **Explore → Loki**, фильтруешь `{service_name="ProjectTraiding.Api"} | level="Error"` в том же временном окне.
   - Видишь конкретные сообщения: «MOEX request failed, error_category=timeout, endpoint=...».

3. У записи лога есть поле `trace_id`. Кликаешь на него → **переход в Tempo**.
   - Видишь весь путь запроса: где он застрял, сколько ждал, что произошло до ошибки.

### Трейс → Лог

Внутри трейса в Tempo можно кликнуть «Logs for this span» (если datasource настроен) — увидишь все логи, записанные в контексте этого span-а.

### Когда переходы не работают

- Если у лога нет `trace_id` — значит, запись произошла вне Activity (ранний старт, фоновый процесс без span-а). Это нормально для технических записей.
- Если кнопка перехода в Tempo отсутствует — проблема в `datasources.yaml`: derived field должен брать `trace_id` из structured metadata, а не regex-ом из текста строки.

---

## 10. Типовые задачи

### «Приложение тормозит» — найти узкое место

1. Prometheus: `histogram_quantile(0.95, sum by (le) (rate(moex_http_request_duration_bucket[5m])))` — латентность выросла?
2. Loki: `{service_name="ProjectTraiding.Api"} |= "MOEX retry attempt"` — есть ли ретраи?
3. Tempo: `{ name = "moex.load" && duration > 5s }` — какие загрузки медленные? Открой водопад, посмотри, где время.

### «Биржа не отвечает» — диагностика

1. Prometheus: `sum by (error_type) (rate(moex_http_errors_total[5m]))` — тип ошибок: `timeout`, `server_error`, `rate_limit`?
2. Loki: `{service_name="ProjectTraiding.Api"} |= "MOEX request failed"` — конкретные endpoint-ы.
3. Prometheus: `sum(rate(moex_ratelimit_queued_total[5m]))` — очередь rate limiter растёт? Возможно, биржа ограничила.

### «Загрузка данных прошла?» — проверка

1. Loki: `{service_name="ProjectTraiding.Api"} |= "MOEX paging stopped"` — остановки пагинации, `stop_reason`, `total_rows`.
2. Prometheus: `sum by (data_kind) (rate(moex_rows_total_total[5m]))` — сколько строк по типам.
3. Prometheus: `sum(rate(moex_rawcapture_writes_total[5m]))` — сколько записей ушло в S3.

### «Сколько ресурсов потребляет приложение?»

1. Prometheus: `process_runtime_dotnet_gc_heap_size_bytes` — размер кучи.
2. Prometheus: `sum by (generation) (rate(process_runtime_dotnet_gc_collections_total[5m]))` — частота сборки мусора.
3. `http://localhost:5025/healthz` — приложение живо.

---

## 11. Частые ошибки и грабли

**Метрик нет в Prometheus.** Нет трафика — нет метрик. Приложение должно сделать реальные вызовы к MOEX, чтобы счётчики и гистограммы начали наполняться. Кроме того, после первого вызова подожди ~15 секунд (интервал scrape).

**`rate()` возвращает пустой результат.** Окно слишком узкое. Расширь: `[1m]` → `[5m]`.

**Имена метрик не совпадают с кодом.** В коде `moex.http.requests` (с точками), в Prometheus `moex_http_requests_total` (подчёркивания + `_total`). Это автоматическая трансформация, а не ошибка.

**Логи есть, но `trace_id` отсутствует.** Запись произошла вне `Activity` — вызывающий код не создал span. Или `Activity.Current` был `null` в момент записи (ранний старт приложения).

**Переход из лога в трейс не работает.** `trace_id` в Loki приходит как structured metadata (через OTLP), а не как текст в теле строки. Derived field в `datasources.yaml` должен ссылаться на поле, а не на regex по тексту.

**Docker Desktop: контейнеры крашатся при старте.** Частая причина — config-файлы монтируются как директории вместо файлов. На Windows монтируем папки целиком (`./otel-collector:/etc/otel:ro`), не отдельные файлы. При проблемах: `docker compose down --remove-orphans` + повторный `up`.

---

## 12. Словарь терминов (A–Z)

**Activity** — .NET-реализация span-а. Создаётся через `ActivitySource.StartActivity()`. OpenTelemetry SDK транслирует в OTLP span.

**ActivitySource** — фабрика span-ов, один экземпляр на модуль. У нас: `ProjectTraiding.Moex`.

**Bucket** — диапазон значений в гистограмме. Prometheus хранит «сколько значений попало в каждый bucket». Из bucket-ов считаются перцентили.

**Collector** — OpenTelemetry Collector. Промежуточный сервис: принимает OTLP, раскладывает по хранилищам. Конфигурация: `infrastructure/otel-collector/config.yaml`.

**Counter** — метрика-счётчик (только растёт). Пример: `moex.http.requests`.

**Derived field** — настройка Loki datasource в Grafana: позволяет извлечь из лога поле (например, `trace_id`) и сделать из него кликабельную ссылку в Tempo.

**Exporter** — модуль Collector-а, который отправляет данные в хранилище. У нас: `otlphttp/loki`, `prometheus` (scrape), `otlp/tempo`, `debug`.

**Gauge** — метрика, которая может расти и падать. Пример: размер кучи (`gc_heap_size_bytes`).

**Grafana** — веб-интерфейс визуализации. Два режима: Explore (ручные запросы) и Dashboards (панели).

**Histogram** — метрика-распределение. Хранит bucket-ы, из которых считают перцентили. Пример: `moex.http.request.duration`.

**Label (метка)** — дополнительное измерение метрики. `source`, `data_kind`, `error_type` — это метки.

**LGTM** — аббревиатура стека: **L**oki + **G**rafana + **T**empo + Prometheus (буква **M** от «metrics»). Иногда пишут LGTM, иногда Grafana LGTM Stack.

**Loki** — хранилище логов от Grafana Labs. Не индексирует полный текст (в отличие от Elasticsearch) — индексирует только метки. Дёшев по ресурсам.

**LogQL** — язык запросов Loki. Формат: `{метки} | фильтры`.

**Meter** — .NET-фабрика метрик. Один экземпляр на модуль. У нас: `ProjectTraiding.Moex`.

**OTLP** — OpenTelemetry Protocol. Формат передачи логов, метрик и трейсов. Поддерживает gRPC и HTTP.

**OpenTelemetry** — вендор-нейтральный стандарт инструментации. Приложение использует его API, а куда данные попадут (Loki, Datadog, ELK) — определяет конфигурация, не код.

**Prometheus** — хранилище метрик. Pull-модель: сам ходит за данными (scrape).

**PromQL** — язык запросов Prometheus. Основа: имя метрики + функции (`rate`, `sum`, `histogram_quantile`).

**Rate** — скорость изменения счётчика (значений в секунду). `rate(counter[5m])` — основная операция в PromQL.

**Receiver** — модуль Collector-а, который принимает данные. У нас: `otlp` на gRPC `:4317`.

**Resource** — набор атрибутов, идентифицирующий источник телеметрии. У нас: `service.name=ProjectTraiding.Api`.

**Scrape** — Prometheus ходит за метриками по HTTP. Interval: 15 секунд. Target: `otel-collector:8889`.

**Span** — один «отрезок» работы внутри трейса. У него есть имя, начало, конец, атрибуты, родительский span.

**Tempo** — хранилище трейсов от Grafana Labs. Хранит только по trace_id, не индексирует атрибуты.

**Trace** — полная картина одной операции: дерево span-ов, объединённых одним `trace_id`.

**TraceQL** — язык запросов Tempo. Формат: `{ условия на span }`.

**trace_id** — 32-символьный hex-идентификатор, общий для всех span-ов и логов одной операции. Клей между потоками.

**Waterfall (водопад)** — визуализация трейса: горизонтальные полоски, вложенные друг в друга, показывающие длительность и вложенность span-ов.

---

## Связь с проектными документами

- **Обзор системы, раздел 5.4** — целевое состояние наблюдаемости, три потока, OpenTelemetry как принцип, LGTM как гипотеза.
- **Правила разработки, правило 11** — стандартное логирование, запрет самодельного слоя.
- **Наблюдаемость — план закрытия фазы 2 v0.3** — каталог событий, метрик, Activity, критерии закрытия.
- **Фаза 2 — задачи реализации v0.4** — 22 задачи реализации.
- **Контракт источника** — EventId 100–151, 160–169.
