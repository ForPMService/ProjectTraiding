# ProjectTraiding — ИИ Алго Трейд (очередь 1)

Кратко:
- Цель: каркас для первой очереди ТЗ_001 "Витрина данных" — контур загрузки, хранения, проверки качества и подготовки рыночных данных для SBER и SILV.
- Важно: реальная торговля запрещена; не реализуются брокерские заявки, торговые стратегии или роботы.

Что реализуется в первой очереди (сkeleton):
- Backend: .NET 10 решение с Web API и фоновым Worker'ом; проект Shared/Contracts.
- Health endpoints: `GET /health/live`, `GET /health/ready` в Web API.
- Infrastructure: docker-compose для локального запуска контейнеров хранения (Postgres, ClickHouse, Redis, MinIO).
- Frontend: папка для будущего Angular-приложения (заготовка).
- Документация: базовые README.

Что НЕ реализуется сейчас:
- Загрузка из MOEX, ClickHouse-схемы, расчёт признаков, торговые компоненты.

Как запустить локально (черново):
1. Убедитесь, что установлены Docker и Docker Compose.
2. Запустить сервисы хранилища:

```bash
cd infrastructure
docker compose up -d
```

3. Для запуска Web API/Worker локально (нужен .NET 10 SDK):

```bash
cd backend/src/ProjectTraiding.Api
dotnet run
```

```bash
cd backend/src/ProjectTraiding.Worker
dotnet run
```

Проверить health:
- http://localhost:5000/health/live
- http://localhost:5000/health/ready

(Порты и детальные настройки можно корректировать в дальнейшем.)

Перед коммитом проверьте раздел "Что проверить перед commit" в корневом README.
