# MOEX Data Operations Console — статическая вёрстка (Angular 21)

Статическая вёрстка операторской консоли (HTML/SCSS) на **Angular 21** с современным
стеком: **zoneless** change detection, **signals**, функции **`input()` / `output()`**,
**`OnPush`** во всех компонентах, standalone-by-default, новый style-guide (имена файлов
без суффикса `.component`).

Без бизнес-логики, HTTP, сервисов, роутера и UI-библиотек. Все данные — мок,
захардкоженный в компонентах. Навигация между экранами — через `output()`
и `@switch` в `Shell`.

## Запуск

```bash
npm install
npm start          # = ng serve
```

Откройте http://localhost:4200

> Требуется Node.js 20.19+ (или 22.12+) — требование Angular 21.
> Минимальная ширина макета — 1440px (адаптива нет по условию задания).

## Что изменилось по сравнению с версией на Angular 18

| Практика | Было (v18) | Стало (v21) |
| --- | --- | --- |
| Change detection | Zone.js | **Zoneless** (`provideZonelessChangeDetection`) |
| Состояние навигации | поле `currentPage: PageKey` | **signal** `currentPage = signal<PageKey>(…)` |
| Хлебная крошка | геттер | **computed** signal |
| Входы / выходы | `@Input()` / `@Output() EventEmitter` | **`input()` / `output()`** |
| Стратегия CD | по умолчанию | **`ChangeDetectionStrategy.OnPush`** везде |
| `standalone: true` | явно указан | опущен (значение по умолчанию) |
| Имена файлов / классов | `shell.component.ts` → `ShellComponent` | `shell.ts` → `Shell` |
| Bootstrap | `bootstrapApplication(App)` | `app.config.ts` + `provideZonelessChangeDetection` |
| Сборщик | — | `@angular/build:application` (esbuild) |

> **Одно осознанное решение:** имена файлов без суффикса `.component` и классы без
> суффикса `Component` — это поведение Angular CLI по умолчанию начиная с v20.
> Если в команде принят старый стиль с суффиксами, его можно вернуть через
> `ng config` / схематики — на архитектуру это не влияет.

## Архитектура навигации

- `currentPage` — единственный источник состояния, живёт **только** в `Shell` (signal).
- Дочерние страницы ничего не знают о навигации: они лишь эмитят `pageChange` (`output<PageKey>()`).
- `Shell` рендерит активную страницу через `@switch (currentPage())` и пишет новое
  значение через `currentPage.set($event)`.

## Структура

```
src/
  app/
    app.ts                      # корневой компонент App
    app.config.ts               # ApplicationConfig (zoneless)
    shared/page-key.ts          # тип PageKey
    layout/
      shell.ts / .html / .scss  # сайдбар + топбар + @switch
    pages/
      dashboard/
      instruments/
        catalog/                # каталог + drawer (input: activeTab, showDrawer)
        card-stock/             # карточка акции
        card-futures/           # карточка фьючерса
  styles/                       # токены, миксины, общие примитивы
  index.html
  main.ts
```

## Экраны

Переключение — кликами внутри интерфейса:

- **Dashboard** — метрики + алерты + таблица загрузок
- **Каталог · Акции / Фьючерсы** — таблицы инструментов, вкладки Все/Акции/Фьючерсы
- **Каталог + Drawer** — боковая панель деталей (клик по action-кнопке в строке)
- **Карточка акции (SBER)** / **Карточка фьючерса (SiM6)** — клик по строке
- Возврат из карточки — «← Назад к инструментам».
