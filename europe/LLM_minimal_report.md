# Европа: минимальный LLM-контур, этап № 2

Реализована обработка `PublicationReceived` через одну bounded-очередь и
фиксированное число постоянных асинхронных потребителей. Контрольная пересылка
оригинала остаётся независимой. Приложение и внешние API не запускались.

## Изменённые файлы

- `src/ProjectTraiding.Europe/News/NewsAgentExecutor.cs` — очередь, потребители,
  локальный приём снимка, один LLM-запрос, техническая проверка и выход результата.
- `src/ProjectTraiding.Europe/News/NewsContracts.cs` — неизменяемые DTO, JSON-контекст
  и строгая схема, выведенная из полей и ограничений контракта v3.
- `src/ProjectTraiding.Europe/News/NewsAgentOptions.cs` — проверка операторских пределов.
- `src/ProjectTraiding.Europe/Program.cs` — регистрация обработчика перед Telegram.
- `src/ProjectTraiding.Europe/ProjectTraiding.Europe.csproj` — официальный адаптер.
- `src/ProjectTraiding.Europe/appsettings.json` — выключенный контур без реквизитов.
- `src/ProjectTraiding.Europe/Telegram/EuropeTelegramRuntime.cs` — только актуализация
  XML-комментария события; поведение приёмника и пересылки не изменено.
- `LLM_minimal_report.md` — этот отчёт.

## Зависимости и подтверждённые API

Единственная новая прямая зависимость — `Microsoft.Extensions.AI.OpenAI` **10.10.1**:
даёт официальный адаптер к стандартному `IChatClient` и готовое преобразование
параметров schema/web search. Полный пакет `Microsoft.Extensions.AI` не добавлен:
его middleware здесь не нужны. Через адаптер разрешились необходимые зависимости:

| Пакет | Версия | Причина |
|---|---|---|
| Microsoft.Extensions.AI.Abstractions | 10.10.1 | IChatClient, ChatOptions, сообщения и HostedWebSearchTool |
| OpenAI | 2.14.0 | официальный Responses SDK, HTTP-вызов и модели протокола |
| System.ClientModel | 1.15.0 | штатный transport/pipeline, credential и отключение retry |
| System.Memory.Data | 10.0.9 | BinaryData, используемый SDK и transport |

`System.Threading.Channels`, `System.Collections.Immutable`, `System.Text.Json` и
SHA-256 предоставляет платформа; отдельных пакетов для них не добавлено.
Версии проверены в NuGet, зависимости — в nuspec и `project.assets.json`.
Проект `net11.0` использует библиотечные assets `net10.0`; совместимость сборки
и AOT подтверждена локальной компиляцией, работоспособность в runtime не проверена.

Проверены исходники именно версии адаптера 10.10.1:
[OpenAIClientExtensions](https://github.com/dotnet/extensions/blob/v10.10.1/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs),
[OpenAIResponsesChatClient](https://github.com/dotnet/extensions/blob/v10.10.1/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs).
Подтверждён следующий путь:

- `ResponsesClient(ApiKeyCredential, ResponsesClientOptions).AsIChatClient()`;
- `IChatClient.GetResponseAsync(messages, ChatOptions, CancellationToken)`;
- `ChatResponseFormat.ForJsonSchema` в `ChatOptions.ResponseFormat` и
  `AdditionalProperties["strict"] = true` преобразуются в
  `ResponseTextFormat.CreateJsonSchemaFormat` с `strict=true`;
- `HostedWebSearchTool` преобразуется в настоящий hosted `WebSearchTool`
  внутри того же Responses-запроса; `ChatToolMode.Auto` оставляет решение модели;
- `RawRepresentationFactory` возвращает свежий `CreateResponseOptions` с
  `StoredOutputEnabled=false`, без conversation/previous response;
- `ResponsesClientOptions.RetryPolicy = new ClientRetryPolicy(0)` отключает
  штатные повторы SDK; `NetworkTimeout` и linked CTS задают конечный срок;
- адаптер сохраняет `ResponseResult` в `RawRepresentation`, а refusal — в
  `ErrorContent`; оба проверяются до предметной выдачи.

Responses SDK и адаптер помечены **OPENAI001**. Для конкретных обращений к ним
локально разрешена эта экспериментальная API-поверхность; AOT/trimming-диагностики
не подавлены. Экспериментальные агенты и оркестраторы не подключались.

Валидный снимок на этом этапе допускает только **gpt-4.1-2025-04-14**.
Поддержку Structured Outputs и этого закреплённого snapshot подтверждает
[страница модели](https://developers.openai.com/api/docs/models/gpt-4.1);
поддержку web search в Responses для семейства GPT-4.1 —
[официальная документация web search](https://developers.openai.com/api/docs/guides/tools-web-search).
Schema и поиск передаются совместно через проверенный адаптер; реальный ответ
этой комбинации, доступ аккаунта и лимиты не проверялись. Поддержка произвольной
модели намеренно не заявляется. Realtime здесь — режим проекта, не аудио Realtime API.

## Локальная конфигурация и граница результата

Контур выключен по умолчанию. При включении нужны переменные среды:
`News__Enabled`, `News__QueueCapacity` (1..65536), `News__Workers` (1..64),
при необходимости `News__RequestTimeout` (положительный, не более 10 минут),
и `OPENAI_API_KEY`. Ключ читается только из среды. Настройки проверяются при старте,
до создания клиента. Размеры очереди и число исполнителей владелец выбирает сам.

`NewsAgentExecutor.UpdatePromptSnapshot(NewsAgentPromptSnapshot)` принимает готовый
полный снимок: Version, PromptVersion, Prompt, Mode=Realtime, EventTypes,
GlobalEvents, Instruments с secid/названием/типом/базовым активом, JsonSchema,
Model, MaxOutputTokens (1..32768), Temperature (0..2), WebSearchEnabled.
Пустые справочники сюжетов и инструментов допустимы; отсутствующие массивы,
пустой промпт, неверный режим/модель/схема отклоняются до замены. Схема должна
соответствовать `NewsAgentJsonContext.ContractSchema`; это структурная часть v3,
а не альтернативный предметный контракт.

Все DTO неизменяемы, коллекции — ImmutableArray. Схема клонируется независимо
от JsonDocument вызывающего кода. После проверки и SHA-256 полного сериализованного
снимка выполняется одна Interlocked.Exchange. `agent_version = news-v3:<hash>`
идентифицирует модель, промпт, режим, инструменты и остальные переданные настройки,
включая версию снимка. Потребитель получает одну ссылку через Volatile.Read
на старте анализа и сохраняет её до окончания. Подмена текущего снимка не меняет
начатый запрос. Неудачная замена оставляет прежний валидный снимок.

Готового промпта, справочников и примера конфигурации в приложении нет.
Без снимка публикация пропускается с кратким предупреждением, LLM не вызывается.
Синхронизация снимка из России относится к следующим этапам.

Подписка выполняется в StartAsync до старта Telegram. Callback делает TryWrite
и при отказе пишет одно краткое предупреждение без данных. Общая очередь использует
Wait + TryWrite: заполнение не блокирует callback, публикация теряется только
для анализа. Нет Task.Run на публикацию, собственного планировщика или SemaphoreSlim.
Успешные новости и JSON не журналируются; новых счётчиков нет.

Каждая допущенная к анализу публикация вызывает GetResponseAsync ровно один раз.
Исходные текст и метаданные сериализуются без смысловой обработки. Поиск опционален
только в Realtime; сервер может выполнить его внутри этого запроса, клиентского
цикла инструментов и повторных HTTP-запросов приложения нет.

Проверяются completed, отсутствие ErrorContent/refusal, соответствие фактической
модели снимку, JSON-типы, обязательные поля, неизвестные/повторные ключи,
accepted/facts, непустые строковые поля, nullable-поля, явная зона occurred_at,
диапазоны оценок и несовместимость двух вариантов global_event.
`accepted=false, facts=[]` выдаётся как нормальный результат. Ошибки API, сети,
таймаут, отказ модели и невалидный JSON остаются техническими ошибками и не создают
предметный ответ. Ошибка подписчика также не объявляется доставкой.
Существование event_type/global_event/secid и уникальность secid внутри факта
окончательно проверяет будущий российский Writer.

Единственный выход — `AnalysisCompleted`: исходная TelegramChannelPost, фактический JSON,
ConfigurationVersion, фактическая Model и вычисленная AgentVersion.
**Подписчика сейчас нет; результат не сохраняется и не считается доставленным.**
При старте об этом есть одно предупреждение. Текущая синхронная точка предполагает
короткого потокобезопасного подписчика без I/O; будущая gRPC-передача должна отдельно
оформить асинхронную транспортную границу. ACK, буфера результатов и фиктивного
потребителя не добавлено. `available_at`/`created_at` здесь не подменяются временем
окончания LLM: по v3 их формирует Writer при предметной записи.

ApplicationStopping отменяет текущие запросы и закрывает допуск независимо от
порядка остановки Telegram. StopAsync снимает подписку и проверяет завершение
потребителей до срока хоста. Очередь при остановке не дренируется: потеря
незавершённых публикаций принята владельцем. У SDK нет фонового polling-режима.
Отмена кооперативна; фактическое соблюдение срока провайдером не проверялось.

## Минимальность и применение правил

Добавлено **11 объявлений типов**: один содержательный компонент, один Options,
восемь DTO-record и один source-generated JSON-контекст. Обоснование каждого:

| Тип | Что теряется без него |
|---|---|
| NewsAgentExecutor | владение очередью/потребителями, изоляция вызова и остановки |
| NewsAgentOptions | типизированная валидация операторских пределов |
| NewsAgentPromptSnapshot | атомарный полный неизменяемый снимок |
| NewsGlobalEventDTO | типизированные id/name существующего сюжета |
| NewsInstrumentContextDTO | обязательный контекст разрешённого инструмента |
| NewsLlmRequest | именованная AOT-упаковка входа без дублирования промпта/схемы |
| NewsLlmResponse | обязательные accepted/facts и проверка их согласованности |
| NewsFactDTO | точная структура факта с nullable-полями и вложенными оценками |
| NewsInstrumentAssessmentDTO | типы и диапазоны оценки инструмента |
| NewsAgentResult | явная внутренняя граница публикация/JSON/версия/модель |
| NewsAgentJsonContext | сериализация и десериализация без прикладной рефлексии |

Подсчёт до/после одинаков: все `*.cs` под `europe/src`, без bin/obj;
строки — сумма `(Get-Content <file>).Count`, включая пустые строки и комментарии;
типы — число строк, совпадающих с `\b(class|record)\s+(?!struct\b)[A-Za-z_]`.
Генерируемые SDK/compiler типы не учитываются.

| Показатель | До | После |
|---|---:|---:|
| Файлы C# | 6 | 9 |
| Строки C# | 431 | 806 |
| Объявления class/record, включая static/JSON-контекст | 5 | 16 |

Три новых файла содержат 366 строк; подключение добавляет 9 строк Program.cs.
Русские XML-комментарии есть у типов и содержательных методов. Горячих LINQ,
анонимных объектов, собственных интерфейсов/фабрик/провайдеров и прикладной рефлексии нет.
Статические проверки/LoggerMessage остаются внутри компонента: отдельный Parser
или слой логирования не имеет собственной нетривиальной ответственности.

Прямые упрощения промпта № 2 имеют приоритет над буквальными правилами v0.2:
№ 19/21/28 — без retry/circuit breaker, собственной иерархии исключений и middleware;
№ 38 — без метрик/observability-инфраструктуры; № 40 — без тестов по решению владельца;
№ 35 — текущие запросы отменяются, остаток очереди теряется;
№ 18/39 — format и benchmarks не входят в разрешённые проверки этого задания.
№ 32 ограничен строгой schema: неизвестные поля предметного ответа отклоняются.
Это явные ограничения этапа, удалённая инфраструктура не восстановлена.

## Выполненные проверки и ограничения

SDK: `11.0.100-rc.1.26425.128`. Команды restore/build/publish выполнены из `europe/`:

```powershell
dotnet --version
dotnet restore .\ProjectTraiding.Europe.slnx
dotnet build .\ProjectTraiding.Europe.slnx -c Release --no-restore
dotnet publish .\src\ProjectTraiding.Europe\ProjectTraiding.Europe.csproj -c Release -r win-x64 -p:PublishAot=true -p:TrimmerSingleWarn=false -p:IlcSingleWarn=false
git diff --check
```

Release build: 0 ошибок, 0 предупреждений; NETSDK1057 сообщает о preview SDK.
Native AOT win-x64: компиляция успешна, результат не выполнялся.
Раскрытые предупреждения: IL2026 — 9, IL3050 — 9, IL2067 — 2; все относятся
к WTelegram/TL, в том числе reflection JSON с выключенной reflection-сериализацией.
Новых предупреждений LLM-кода/SDK/адаптера не обнаружено. Предупреждения не подавлены;
PublishAot, IsAotCompatible и запрет reflection JSON сохранены.
Вывод AOT-компиляции находится в `%TEMP%/ProjectTraiding-Europe-News-AOT.log`.

Статически проверены порядок подписки, одна общая очередь, единственная точка
LLM-вызова, отключение retry, фиксация снимка, изоляция ошибок и отмена.
Проверок работы провайдера не было: не подтверждены реальный structured JSON,
совместный web search, стоимость/лимиты/права аккаунта, timeout/cancellation,
перегрузка и остановка в runtime. Linux Native AOT не компилировался.
Сборка не доказывает работу LLM, поиска, Telegram или библиотек в живой AOT-среде.

Тесты/моки/демо не создавались. Не выполнялись dotnet run/dotnet test,
авторизация или запросы Telegram/LLM, обращение к PostgreSQL, запуск процессов
приложений и развёртывание. Сеть использовалась только для git/NuGet/документации
и исходников библиотек. `backend/` не изменён.

## Расхождения с прежними документами

План v1.3/прежний контракт v1.0 описывали более широкий контур: управляющий бот,
динамический каталог каналов и инфраструктуру доставки. Бот и динамические каналы
уже удалены предыдущим решением владельца; здесь не восстанавливаются.
Действующий предметный контракт — упрощённый v3: вложенные instruments,
без fact_index, склейки и придуманной схемы. Потеря незавершённых публикаций
при аварии/перегрузке/остановке на этом этапе принята. Старые README и документы
не исправлялись молча; текущее состояние описано отдельным отчётом.

Выявленная ранее проблема учёта незавершённого Telegram RPC остаётся открытой:
LLM-контур её не исправляет и не маскирует. gRPC, Writer, российская конфигурация,
сохранение/ACK и восстановление принадлежат следующим этапам. Промпт № 3 не начат.
Чужое удаление `Задание_Telegram_приёмник_усиленное_v2.md` и неотслеживаемый файл
правил в docs сохранены и не включаются в коммит. Работа выполняется в main,
одним коммитом с обычной отправкой в origin/main, без веток и PR.
