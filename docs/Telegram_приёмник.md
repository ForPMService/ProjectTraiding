# Telegram: приёмник первого этапа

Модуль выключен по умолчанию. `Api` регистрирует один `TelegramAccountSession`
и одну фоновую службу. Для приёма используется `WTelegramClient 4.4.9`;
пакет `Telegram.Bot 22.10.3.2` сохранён без использования его типов.

## Настройка и вход

В `appsettings.json` нужно заполнить пути и публичные имена каналов перед
включением. `ApiId`, `ApiHash`, `PhoneNumber` поступают из User Secrets в
Development или секретов окружения при поставке:

```text
Telegram__Enabled=true
Telegram__ApiId=<api_id>
Telegram__ApiHash=<api_hash>
Telegram__PhoneNumber=<phone_number>
Telegram__SessionPath=<абсолютный путь к account.session>
Telegram__UpdateStatePath=<абсолютный путь к updates.state>
Telegram__Channels__0=<публичное имя первого канала>
Telegram__Channels__1=<публичное имя второго канала>
```

Пути должны быть разными, в постоянном каталоге вне публикации и Git.
В границах этого проекта подходит игнорируемый каталог
`C:\Users\89263\Desktop\Project\ProjectTraiding\storage\telegram\`.
Поставке нужен постоянный каталог с ограниченным доступом. Одновременное
использование одного файла сессии двумя процессами не поддерживается.
Валидация отклоняет относительные пути, каталог исполняемых файлов и `.git`;
исключение данных из Git и сохранность каталога обеспечивает оператор.

`OutboundProxyUrl` должен оставаться `null`: непустое значение означает
`Error` до создания клиента, без прямого подключения. Глобальные переменные
прокси и настройки клиентов Moex не изменяются.

Оба маршрута доступны в Development и Release. GET только читает статус:

```http
GET /telegram/auth/status
```

Пример ответа при необходимости кода:

```json
{"state":"VerificationCodeRequired","expectedField":"verification_code"}
```

POST передаёт ответ тому же клиенту. `expectedField` берётся из статуса:

```http
POST /telegram/auth/answer
Content-Type: application/json

{"expectedField":"verification_code","value":"<код из Telegram>"}
```

Возможные поля: `verification_code`, `password`, `email`, `email_verification_code`.
Ответ содержит только `state` и `expectedField`. Пустой ввод получает `400`,
параллельный, повторный или несоответствующий шагу ввод — `409`, исключение
продолжения входа — `503` и `Error`. Неверный код или пароль завершает попытку;
для новой попытки нужен перезапуск Api. Регистрация аккаунта не поддерживается.
Тело ограничено 4096 байтами. Значения не сохраняются в конфигурации и журнале.
Реальные коды и пароли не следует помещать в историю команд или HTTP-журнал.

Маршруты проверяют реальный `Connection.RemoteIpAddress` через
`IPAddress.IsLoopback`; нелокальному адресу возвращается `403` до чтения тела.
`Host` и `X-Forwarded-For` не используются. Для Release предполагается SSH-туннель
к существующему локальному HTTP-интерфейсу. Публичный обратный прокси, подключённый
к Kestrel через loopback, делает этот фильтр недостаточным: такое развёртывание
не поддерживается в данном этапе.

## Приём и жизненный цикл

После `Authorized` заново загружаются `Messages_GetAllDialogs`, вызывается
`CollectUsersChats`, сопоставляются доступные аккаунту публичные `broadcast`-каналы
по `username` без учёта регистра. Отсутствие любого канала означает `Error`;
его имя записывается в журнал, приём не включается. Супергруппа не заменяет канал.

`Receiving` и `readyAtUtc` устанавливаются после полного сопоставления.
Принимается только `UpdateNewMessage`, включая `UpdateNewChannelMessage`,
с `TL.Message`, `PeerChannel`, разрешённым идентификатором, непустым текстом
и `PublishedAt > readyAtUtc`. До готовности, старые сообщения и сообщения той
же секунды не выдаются. Правки, удаления, реакции, служебные сообщения, группы,
личные чаты и медиа без текста пропускаются. Файлы не скачиваются.

`TelegramChannelPost` содержит семь полей без типов WTelegram. Журнал включает
идентификаторы, имя и название канала, два времени UTC и их `Kind`. Полный текст
журналируется только в среде Development. LLM, `CustomFeatures.News`, PostgreSQL,
прокси и история в реализацию не входят. Доставка «ровно один раз» не обещается.

При остановке выдача прекращается, освобождается клиент, затем вызывается
`UpdateManager.SaveState`. Очистка после сбоя callback выполняется службой,
чтобы не сохранять состояние под внутренним semaphore менеджера обновлений.
Глобальная политика фоновых служб не меняется.

Сверены исходники установленной версии 4.4.9, commit
`187c773a8f875db9f4f76e0778a33d025c0b70a1`:
[Client.Login](https://github.com/wiz0u/WTelegramClient/blob/187c773a8f875db9f4f76e0778a33d025c0b70a1/src/Client.cs),
[UpdateManager](https://github.com/wiz0u/WTelegramClient/blob/187c773a8f875db9f4f76e0778a33d025c0b70a1/src/UpdateManager.cs),
[пример завершения](https://github.com/wiz0u/WTelegramClient/blob/187c773a8f875db9f4f76e0778a33d025c0b70a1/Examples/Program_ListenUpdates.cs),
[UTC в ReadTLStamp](https://github.com/wiz0u/WTelegramClient/blob/187c773a8f875db9f4f76e0778a33d025c0b70a1/src/TL.cs).

## Проверки 8 октября 2026

Ветка `feature/telegram-channel-receiver`, исходное состояние `main` (`b3410cc`).
`Telegram_Architecture_v1_2_2.md` внутри разрешённой папки не найден. Сверка с ним
не выполнена; реализация основана на приложенном задании усиленной редакции v2.

| Шаг | Запущено | Результат |
|---|---|---|
| Каркас, `9dbdd43` | `dotnet build backend/ProjectTraiding.slnx` | Код 0; пять предупреждений существующего кода |
| Каркас, `9dbdd43` | `dotnet publish backend/src/ProjectTraiding.Api/ProjectTraiding.Api.csproj -c Release` | Код 0, NativeAOT; предупреждения ClickHouse.Driver и Microsoft.IO.RecyclableMemoryStream |
| Авторизация, `1dcbc8a` | Сборка solution | Код 0; последний запуск без предупреждений |
| Авторизация | Запуск Api с Telegram выключенным | GET: 200/Disabled; POST: 409/Disabled; healthz: 200/Healthy; службы Moex запустились |
| Авторизация | Непустой OutboundProxyUrl | GET: 200/Error; журнал: клиент не создан; healthz: 200/Healthy; службы Moex запустились |
| Полный приёмник | Сборка solution | Код 0; в последней сборке два предупреждения существующего кода |
| Полный приёмник | Release-публикация | Код 0, NativeAOT; дополнительно IL2104/IL3053 WTelegramClient |
| Полный приёмник | Повторная публикация с `-p:TrimmerSingleWarn=false` | Код 0; раскрыты IL2026/IL3050 библиотеки в Session, UpdateManager и Helpers, включая JsonStringEnumConverter |
| Полный приёмник | Опубликованный NativeAOT exe в Production, Telegram выключен | GET: 200/Disabled; POST: 409/Disabled; healthz: 200/Healthy; службы Moex запустились |
| Полный приёмник | Telegram выключен, ApiId содержит некорректное неиспользуемое значение | GET: 200/Disabled; клиент не создан |
| Полный приёмник | Опубликованный NativeAOT exe в Development, Telegram выключен с некорректным ApiId | GET: 200/Disabled; POST: 409/Disabled |
| Полный приёмник | Опубликованный NativeAOT exe в Production, непустой OutboundProxyUrl | GET: 200/Error; healthz: 200/Healthy; журнал: клиент не создан; службы Moex запустились |
| Полный приёмник | Ошибка привязки ApiId при включённом модуле | GET: 200/Error; healthz: 200/Healthy; службы Moex запустились |
| Полный приёмник | POST при отсутствии активного входа / с некорректным JSON | 409/Error / 400/Error; пробные значения конфигурации и тела отсутствуют в журналах |

Для сборок использовался `-p:RestoreConfigFile=<внутренний NuGet.Config>`.
Кэши NuGet, `DOTNET_CLI_HOME`, временные файлы и каталоги профиля перенаправлены
в `tmp/telegram-work` внутри проекта. NativeAOT и запрет JSON-отражения
не отключались; предупреждения не подавлялись. Полный вывод находится в
`tmp/telegram-work/commit1-build.log`, `commit1-publish.log`, `commit2-build.log`,
`commit3-build.log`, `commit3-publish.log`, `commit3-publish-detail.log`.

Живая приёмка **не завершена**. В доступном окружении и настройках Development
конфигурации Telegram нет; User Secrets вне разрешённой папки не читались.
Первый вход, 2FA, повторный запуск с сессией, новые посты каждого канала,
фильтрация живых updates, ошибка отсутствующего канала и сохранение состояния
после активного соединения не подтверждены. Прямое соединение с Telegram
не проверялось. Предупреждения NativeAOT сохраняют необходимость проверки
активного клиента на опубликованном exe.

Нелокальные HTTP-пробы через сетевые адреса машины не дали HTTP-ответа.
Реальный `403` не подтверждён; фильтр проверен чтением кода. Эти проверки
подтверждают запуск служб Moex и независимую доступность Api, но не получение
ими рыночных данных.
