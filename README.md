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
