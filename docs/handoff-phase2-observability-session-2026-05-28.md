# Хэндофф: реализация фазы 2 наблюдаемости — сессия 2026-05-28

Дата: 2026-05-28
Предыдущий хэндофф: success-path capture сессия 2026-05-28 (S3 raw-capture)
Репозиторий: https://github.com/ForPMService/ProjectTraiding

---

## Что было сделано

### Проектные документы

Создано два документа:

1. **Наблюдаемость — план закрытия фазы 2 v0.3** — каталог лог-событий (100–169), диапазоны EventId, правила уровней, Activity-каталог, общие атрибуты, запрещённые поля, каталог метрик, шаги 2.1–2.5, 8 критериев закрытия.

2. **Фаза 2 — задачи реализации v0.4** — 22 задачи, прошли 4 раунда ревью. Финальная версия принята к реализации.

### Архитектурные решения

- **[DECISION] Отдельный модуль `ProjectTraiding.Observability`** — хостовый инфраструктурный модуль настройки OpenTelemetry. Не самодельный логгер. Обоснование: правило 14 (разделение кода), правило 13 (защищает правило + управляет ресурсом + отделяет внешний SDK).

- **[DECISION] Moex не ссылается на Observability** — предметный модуль не зависит от инфраструктурной настройки. Общие атрибуты телеметрии временно дублируются в `MoexTelemetryAttributes`. При появлении второго модуля — выносятся в `ProjectTraiding.Telemetry.Abstractions`.

- **[DECISION] Prometheus через scrape** — Collector открывает prometheus exporter на порту 8889, Prometheus scrape-ит. Проще remote write для Docker Compose.

- **[DECISION] Docker-образы с зафиксированными версиями** — otel-collector-contrib:0.120.0, loki:3.5.0, prometheus:v3.3.0, tempo:2.7.0, grafana:11.6.0.

- **[DECISION] `IsAotCompatible=true`** для библиотек (Observability), `PublishAot=true` только в исполняемом Api.

- **[DECISION] Health checks разделены** — Observability даёт механизм (`AddHealthChecks()`) и endpoint (`/healthz`). Конкретные проверки (PostgreSQL, Garage) — в хосте.

- **[DECISION] `MoexTelemetry` вместо `MoexActivitySource`** — единая точка входа с `ActivitySourceName`, `MeterName`, `ActivitySource`. Будущие модули повторяют паттерн.

### Реализованный код — шаг 2.1 (инструментация)

**Новый модуль `ProjectTraiding.Observability`:**
- `ProjectTraiding.Observability.csproj` — 6 OpenTelemetry-пакетов v1.15.x, `IsAotCompatible=true`, `JsonSerializerIsReflectionEnabledByDefault=false`.
- `ObservabilityServiceCollectionExtensions.cs` — `AddProjectTraidingObservability(builder, activitySources[], meters[])` + `MapObservabilityEndpoints(app)`.
  - Tracing + Metrics через `builder.Services.AddOpenTelemetry()`.
  - Logging через `builder.Logging.AddOpenTelemetry()` отдельно.
  - `SetResourceBuilder` для logging — logs/traces/metrics один `service.name`.
  - Service name: `OTEL_SERVICE_NAME` → default `ProjectTraiding.Api`. `string.IsNullOrWhiteSpace` для пустого env var.
  - `ArgumentNullException.ThrowIfNull` на builder, activitySources, meters.
  - Health checks: базовый механизм + `/healthz`.

**Телеметрия в `ProjectTraiding.Moex`:**
- `MoexTelemetry.cs` — `ActivitySourceName`, `MeterName`, `ActivitySource`.
- `MoexTelemetryAttributes.cs` — 11 констант: `Source`, `DataKind`, `Secid`, `Market`, `EndpointTemplate`, `ErrorType`, `StatusCode`, `ObjectKey`, `BodySize`, `CaptureMode`, `Success`.
- `MoexMetrics.cs` — 13 инструментов: 4 HTTP, 3 rate limiter, 2 пагинация, 3 raw-capture.

**Обновлённые handler-ы/writer с метриками и Activity:**
- `MoexHttpLoggingHandler.cs` — `HttpRequests`, `HttpRequestDuration`, `HttpErrors` + cancellation fix (`OperationCanceledException` не считается `transport_error`).
- `MoexRateLimitHandler.cs` — `RateLimitAcquired`, `RateLimitWaitDuration`, `RateLimitQueued`.
- `MoexRawCaptureWriter.cs` — `RawCaptureWrites`, `RawCaptureBytes`, `RawCaptureErrors` + `Activity("moex.rawcapture")` + cancellation fix (не глотает `OperationCanceledException`).
- `OnRetryHandler` в `MoexClientServiceCollectionExtensions.cs` — `MoexMetrics.HttpRetries`.

**Все 25 клиентских методов (4 клиента) обновлены:**
- `Activity("moex.load")` с `source`, `data_kind`, `market` в начале каждого метода.
- `PagesTotal` / `RowsTotal` метрики рядом с `PageReceived` / `SinglePageReceived`.
- `SetTag("total_pages"/"total_rows")` после завершения цикла пагинации в `IAsyncEnumerable`-методах.

**`Program.cs`:**
- `builder.AddProjectTraidingObservability(activitySources: [MoexTelemetry.ActivitySourceName], meters: [MoexTelemetry.MeterName])`.
- `app.MapObservabilityEndpoints()`.

**`ProjectTraiding.slnx`** — Observability добавлен.
**`ProjectTraiding.Api.csproj`** — `ProjectReference` на Observability.

### Проверки кода

- **`dotnet build`** — 0 ошибок, 3 warnings CS8620 (старые, nullable Dictionary, не от OpenTelemetry).
- **`dotnet publish` AOT** — 0 ошибок, 0 новых AOT/trim warnings от OpenTelemetry.

### Реализованная инфраструктура — шаги 2.2–2.3 (Collector + LGTM)

**Шаг 2.2 — Collector:**
- `infrastructure/otel-collector/config.yaml` — OTLP receiver, batch processor, `health_check` extension (порт 13133).
- Smoke-проверка шага 2.2 прошла: Collector принимает все три потока, `trace_id` связывает spans, `service.name=ProjectTraiding.Api`.

**Шаг 2.3 — LGTM:**
- `infrastructure/loki/config.yaml` — single-process, filesystem, TSDB v13, `allow_structured_metadata`.
- `infrastructure/prometheus/prometheus.yml` — scrape `otel-collector:8889`.
- `infrastructure/tempo/config.yaml` — single-process, local backend, OTLP gRPC.
- `infrastructure/grafana/provisioning/datasources/datasources.yaml` — Loki/Prometheus/Tempo с cross-links.
- `infrastructure/grafana/provisioning/dashboards/dashboards.yaml` — provisioning config (JSON-дашбордов пока нет).
- `infrastructure/docker-compose.yml` — 7 сервисов (postgres, garage, otel-collector, loki, prometheus, tempo, grafana), 6 named volumes.
- `infrastructure/env.example` — добавлены OTLP-переменные.

**Collector config обновлён для LGTM:**
- Экспортеры: `otlphttp/loki`, `prometheus` (scrape endpoint 8889), `otlp/tempo`, `debug`.

### Smoke-проверка шага 2.3

Все 7 контейнеров running. Grafana Explore → Loki: логи видны с `service_name=ProjectTraiding.Api`, 14 записей, включая:
- `MOEX load started`
- `MOEX HTTP response received`
- `MOEX single page processed`

Контур App → OTLP → Collector → Loki → Grafana подтверждён.

### Проблемы при реализации

**Docker Desktop Windows mount.** Файлы config.yaml монтировались как директории. Решение: монтировать папки целиком (`./otel-collector:/etc/otel:ro`) вместо отдельных файлов (`./otel-collector/config.yaml:/etc/otelcol-contrib/config.yaml:ro`). Также потребовалось изменить command path для otel-collector: `--config=/etc/otel/config.yaml` вместо `/etc/otelcol-contrib/config.yaml`.

**Именование скачанных файлов.** При скачивании файлы получили суффиксные имена (loki-config.yaml, tempo-config.yaml) из-за коллизии имён. Потребовалось переименование в config.yaml. Docker закэшировал старый mount-тип — потребовался `docker compose down --remove-orphans` + повторный `up`.

---

## Текущее состояние репозитория

### Структура модулей

```
ProjectTraiding.Api
  references: Moex, Observability
  Program.cs: AddProjectTraidingObservability + MapObservabilityEndpoints

ProjectTraiding.Observability (новый)
  configures: OpenTelemetry, OTLP, Resource, Health checks
  does NOT know: PostgreSQL, Garage, конкретные зависимости

ProjectTraiding.Moex
  exposes: MoexTelemetry, MoexTelemetryAttributes, MoexMetrics
  uses: System.Diagnostics (BCL only)
  does NOT reference: Observability
```

### Docker Compose (infrastructure/)

```
postgres:18.4          — running, healthy
garage:v2.3.0          — running
otel-collector:0.120.0 — running, health :13133
loki:3.5.0             — running
prometheus:v3.3.0      — running, scrape :8889
tempo:2.7.0            — running
grafana:11.6.0         — running, anonymous admin, :3000
```

### Файлы инфраструктуры в репо

```
infrastructure/
  docker-compose.yml
  env.example
  garage/garage.toml
  otel-collector/config.yaml
  loki/config.yaml
  prometheus/prometheus.yml
  tempo/config.yaml
  grafana/provisioning/datasources/datasources.yaml
  grafana/provisioning/dashboards/dashboards.yaml
```

---

## Что осталось в фазе 2

### Задача 20–21: Grafana-дашборды (шаг 2.4)

Три JSON-дашборда, provisioned из репозитория:

1. **moex-source-health.json** — HTTP запросы/латентность/ошибки/retry к MOEX, rate limiter.
2. **moex-data-loads.json** — загруженные страницы/строки, raw-capture.
3. **app-health.json** — runtime (.NET GC, memory, threads), HTTP к API, health checks.

Способ: строить в Grafana UI → экспортировать JSON → положить в `infrastructure/grafana/provisioning/dashboards/`.

### Задача 22: Документация (шаг 2.5)

Внести диапазон 160–169 (raw-capture) в контракт источника.

### Финальная сверка

8 критериев закрытия фазы 2 (план v0.3, раздел 6):

| # | Критерий | Статус |
|---|---|---|
| 1 | ILogger-логи через OTLP exporter | ✅ подтверждено в Loki |
| 2 | trace_id и span_id в логах | ✅ подтверждено в Collector debug |
| 3 | Метрики через Meter | ✅ moex.* видны в Collector debug |
| 4 | Три потока через OTLP в Collector | ✅ traces + metrics + logs |
| 5 | Collector → Loki/Prometheus/Tempo | ✅ Loki подтверждён, Prometheus/Tempo проверить |
| 6 | Три дашборда в Grafana | ❌ задачи 20–21 |
| 7 | Нет самодельного слоя логирования | ✅ |
| 8 | Каталог зафиксирован | ❌ задача 22 |

### Что проверить в следующей сессии перед дашбордами

1. **Prometheus Explore** — `moex_http_requests_total` или аналогичная метрика в Grafana → Prometheus.
2. **Tempo Explore** — трейсы с `moex.load` span.
3. **Переход лог → трейс** — кликнуть на trace_id в логе Loki, проверить переход в Tempo. Если `matcherRegex` в datasources.yaml не совпадает с форматом — поправить.

---

## После фазы 2

По роадмапу следующая — **фаза 4: хранение и качество данных**. Разведка:
- Граница S3 / PostgreSQL / ClickHouse.
- Форма хранения рыночных данных.
- Состав проверок качества.
- Роль объектного хранилища vs база.

---

## Ключевые технические детали для следующей сессии

- OpenTelemetry пакеты: v1.15.3 (core), v1.15.2 (AspNetCore), v1.15.1 (Http, Runtime).
- OTLP endpoint: `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`.
- Приложение: `http://localhost:5025`.
- Grafana: `http://localhost:3000` (anonymous admin).
- Prometheus: `http://localhost:9090`.
- Collector health: `http://localhost:13133/`.
- Docker Compose: `cd infrastructure && docker compose up -d`.
- Запуск приложения: `cd backend/src/ProjectTraiding.Api && $env:OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317" && $env:OTEL_SERVICE_NAME="ProjectTraiding.Api" && dotnet run`.
- Docker Desktop Windows: config-файлы монтируются папками (`./otel-collector:/etc/otel:ro`), не отдельными файлами.
