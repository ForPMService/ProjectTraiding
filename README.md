# ProjectTraiding — ИИ Алго Трейд (очередь 1)

Кратко:
- Цель: каркас для первой очереди ТЗ_001 "Витрина данных" — контур загрузки, хранения, проверки качества и подготовки рыночных данных для SBER и SILV.
- Важно: реальная торговля запрещена; не реализуются брокерские заявки, торговые стратегии или роботы.

Что реализуется в первой очереди (скелет):
- Backend: .NET 10 решение с Web API и фоновым Worker'ом; проект Shared/Contracts.
- Health endpoints: `GET /health/live`, `GET /health/ready` в Web API.
- Infrastructure: docker-compose для локального запуска контейнеров хранения (Postgres, ClickHouse, Redis, MinIO).
- Frontend: папка для будущего Angular-приложения (заготовка).
- Документация: базовые README.

Что НЕ реализуется сейчас:
- Загрузка из MOEX, ClickHouse-схемы, расчёт признаков, торговые компоненты.

Как запустить локально (черново):
1. Убедитесь, что установлены Docker и Docker Compose, а также .NET 10 SDK для запуска бэкенда.

2. Запустить сервисы хранения (в каталоге `infrastructure`):

```bash
cd infrastructure
docker compose config
docker compose up -d
docker compose ps
```
`.env.example` — это шаблон для переопределения дефолтных значений Docker Compose. Для обычного локального запуска копировать его в `.env` необязательно, потому что в `docker-compose.yml` заданы безопасные локальные значения по умолчанию.

3. Для сборки и запуска backend (пример команд):

```bash
dotnet build .\backend\ProjectTraiding.slnx
dotnet run --project .\backend\src\ProjectTraiding.Api\ProjectTraiding.Api.csproj
```

4. При запуске `dotnet run` смотрите вывод — в нём указана строка типа `Now listening on: http://localhost:52576`. Используйте опубликованный там порт при проверке health.

5. Команды для проверки (замените `<PORT>` на порт из вывода `dotnet run`):

```bash
curl http://localhost:<PORT>/health/live
curl http://localhost:<PORT>/health/ready

cd infrastructure
docker compose config
docker compose up -d
docker compose ps
docker compose exec postgres pg_isready -U projecttraiding -d projecttraiding
docker compose exec redis redis-cli ping
curl http://localhost:8123/ping
curl http://localhost:9002/minio/health/live
```

Что проверить перед commit:
- `dotnet build` для решения проходит без ошибок;
- health endpoints (`/health/live`, `/health/ready`) отвечают 200 на порту, указанном в выводе `dotnet run`;
- `docker compose config` проходит без ошибок;
- все контейнеры подняты и находятся в состоянии healthy (или `Up` с ожидаемым health);
- в коммите не попадают `bin/`, `obj/`, `.vs/`, файлы `.env` (см. `.gitignore`).

Примечание: разделы с тестированием и проверками описаны кратко — при необходимости можно расширить инструкции для конкретной ОС или окружения.

---

## Backend configuration skeleton

- Добавлен проект `ProjectTraiding.Shared` с классами опций (`PostgresOptions`, `ClickHouseOptions`, `RedisOptions`, `ObjectStorageOptions`).
- В `ProjectTraiding.Contracts` добавлены базовые DTO для health и общих ответов (`HealthStatusResponse`, `ServiceHealthItem`, `ErrorResponse`, `JobIdResponse`).
- `appsettings.Development.json` в `ProjectTraiding.Api` содержит локальные dev-настройки для запуска с `docker compose` (Postgres, ClickHouse, Redis, MinIO). Этот файл коммитится намеренно и содержит только локальные значения.
 - Endpoint `/health/ready` реализует двухфазную проверку readiness: сначала проверяется корректность конфигурации (binding через `IOptions<T>`), и только при успешной конфигурации выполняется проверка инфраструктуры (PostgreSQL, Redis, ClickHouse и MinIO при `OBJECT_STORAGE_PROVIDER=minio`). Подробно — в разделе "Health endpoints" ниже.
- Архитектурные решения (на будущее):
	- PostgreSQL будет использоваться через прямые, параметризованные SQL-запросы (Npgsql) — без EF Core, без Dapper и без других ORM.
	- Миграции PostgreSQL будут в виде версионированных SQL-файлов и отдельного runner'а, не через EF Core migrations.
	- ClickHouse будет использоваться через прямой SQL (HTTP-интерфейс или native клиент).

- Примечание по MinIO: MinIO API доступен на `http://localhost:9002` (порт 9000 занят ClickHouse Native Protocol в текущей конфигурации).

- Порт API определяется в выводе `dotnet run` (строка вида `Now listening on: http://localhost:52576`) или через `launchSettings.json` — не предполагается фиксированный порт `5000`.

## Health endpoints

Ниже приведено поведение health endpoints и примеры использования.

- Примеры (замените `<PORT>` на порт из вывода `dotnet run`):

```bash
curl -i http://localhost:<PORT>/health/live
curl -i http://localhost:<PORT>/health/ready
```

- `/health/live`:
	- Проверяет только живость процесса API.
	- Не делает подключений к PostgreSQL, Redis, ClickHouse и MinIO.
	- Возвращает HTTP 200 при живом процессе.

- `/health/ready` (двухфазная проверка):
	1. Сначала выполняется проверка конфигурации (binding через `IOptions<T>`). Если конфигурация содержит ошибки — возвращается HTTP 503 и проверка инфраструктуры не выполняется.
	2. Если конфигурация корректна — выполняются проверки инфраструктуры в следующем порядке: PostgreSQL, Redis, ClickHouse, MinIO (MinIO только при `OBJECT_STORAGE_PROVIDER=minio`).
	- Итоговый статус формируется так (сравнение регистронезависимо):
		- если все сервисы `ok` → `ready`;
		- если есть хотя бы один `unhealthy` → `unhealthy`;
		- если есть `degraded` и нет `unhealthy` → `degraded`.
	- `/health/ready` возвращает HTTP 200 только при итоговом статусе `ready`; иначе HTTP 503.

	- При возникновении исключения во время проверки инфраструктуры сервис логирует ошибку и возвращает HTTP 503. Тело ответа содержит все элементы из проверки конфигурации и дополнительный элемент:

```json
{
	"name": "infrastructure",
	"status": "unhealthy",
	"message": "health check failed",
	"errorCode": "internal_error"
}
```

	Ответы не содержат стек-трейсов, сообщений исключений, строк подключения или секретов.

- Примеры настроек хостов/эндпоинтов:
	- Для локального запуска через `dotnet run` на Windows (пример):

		- `POSTGRES_HOST=localhost`
		- `REDIS_HOST=localhost`
		- `CLICKHOUSE_HOST=localhost`
		- `MINIO_ENDPOINT=http://localhost:9002`

	- Для запуска с `docker compose` (имена сервисов из `docker-compose.yml`):

		- `POSTGRES_HOST=postgres`
		- `REDIS_HOST=redis`
		- `CLICKHOUSE_HOST=clickhouse`
		- `MINIO_ENDPOINT=http://minio:9000`

	MinIO проверяется только при `OBJECT_STORAGE_PROVIDER=minio`; при `local` MinIO не требуется.

Примеры команд проверки (Windows PowerShell — используйте `curl.exe` для корректного поведения):

```powershell
dotnet build .\backend\ProjectTraiding.slnx
dotnet run --project .\backend\src\ProjectTraiding.Api\ProjectTraiding.Api.csproj

curl.exe http://localhost:<PORT>/health/live
curl.exe http://localhost:<PORT>/health/ready

Invoke-RestMethod http://localhost:<PORT>/health/ready | ConvertTo-Json -Depth 5
```

Коротко: backend-каркас и health endpoints уже включают двухфазную readiness-проверку: сначала проверяется конфигурация, затем доступность PostgreSQL, Redis, ClickHouse и ObjectStorage/MinIO. Бизнес-логика загрузки данных, схемы витрины, миграции и торговые компоненты в этом шаге не реализуются.
