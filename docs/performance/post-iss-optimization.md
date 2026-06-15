# Performance: post-iss-optimization

| Параметр | Значение |
|---|---|
| Дата | 2026-06-04 12:54:16 |
| OS | Майкрософт Windows 11 Домашняя |
| CPU | Intel(R) Core(TM) i9-9900KF CPU @ 3.60GHz |
| RAM | 31.9 GB |
| .NET | 10.0.300 |
| Git | `main` @ `ab283b1` |
| Endpoints | 16 |
| Итерации | 3 |
| Timeout | 120 сек |
| AppLogPath | NO_LOG_PATH |
| LogEvidenceWait | 3000 ms |

## Instrument Cards

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| instrument-cards/stock | 155 | 153 | 50 | 1065 | 262 | 137 KB | 0 | 0/3 | NO_LOG_PATH |
| instrument-cards/futures | 259 | 257 | 175 | 521 | 549 | 324 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK Candles

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetCandlesAsset | 270932 | 220 | 266202 | 277522 | 369377 | 54.1 MB | 0 | 0/3 | NO_LOG_PATH |
| GetCandlesFutures | 25800 | 45 | 22379 | 30158 | 75414 | 9.7 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK FUTOI

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetFutoi | 210 | 32 | 178 | 247 | 2194 | 555 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK SuperCandles EQ

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetSuperCandlesTradeStats | 62229 | 285 | 58824 | 63227 | 103694 | 42 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesOrderStats | 1576530 | 1660 | 1325107 | 1607238 | 73182 | 35.7 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesOrderBookStats | 98175 | 435 | 85527 | 100358 | 103520 | 38.5 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK SuperCandles FO

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetSuperCandlesFuturesTradeStats | 4277 | 204 | 2268 | 6965 | 11730 | 5.9 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesFuturesOrderBookStat | 3989 | 362 | 3897 | 4365 | 14042 | 7.3 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK HI2

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetHi2Asset | 226 | 33 | 102 | 259 | 1210 | 176 KB | 0 | 0/3 | NO_LOG_PATH |
| GetHi2Furure | 53 | 43 | 35 | 196 | 715 | 116 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK MegaAlerts

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetMegaAlerts | 885 | 158 | 585 | 1103 | 4435 | 1.8 MB | 0 | 0/3 | NO_LOG_PATH |
| GetMegaAlertsFutures | 351 | 340 | 141 | 405 | 270 | 116 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/stock-offdays | 162 | 161 | 139 | 460 | 88 | 10 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/futures-offdays | 146 | 146 | 28 | 484 | 88 | 10 KB | 0 | 0/3 | NO_LOG_PATH |

## Детали по прогонам

| Endpoint | # | мс | TTFB мс | Строк | Байт | Mem до MB | Mem после MB | Дельта MB | Логи | Log bytes | Pattern | Ошибка |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| instrument-cards/stock | 1 | 1065 | 1062 | 262 | 140754 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| instrument-cards/stock | 2 | 155 | 153 | 262 | 140754 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| instrument-cards/stock | 3 | 50 | 49 | 262 | 140754 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| instrument-cards/futures | 1 | 175 | 174 | 549 | 331569 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| instrument-cards/futures | 2 | 521 | 520 | 549 | 331569 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| instrument-cards/futures | 3 | 259 | 257 | 549 | 331569 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 1 | 266202 | 27 | 369377 | 56708303 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 2 | 270932 | 220 | 369377 | 56708303 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 3 | 277522 | 429 | 369377 | 56708303 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 1 | 30158 | 213 | 75414 | 10138466 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 2 | 25800 | 45 | 75414 | 10138466 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 3 | 22379 | 31 | 75414 | 10138466 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 1 | 210 | 32 | 2194 | 568479 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 2 | 247 | 67 | 2194 | 568479 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 3 | 178 | 23 | 2194 | 568479 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 1 | 58824 | 272 | 103694 | 44089257 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 2 | 63227 | 315 | 103694 | 44089257 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 3 | 62229 | 285 | 103694 | 44089257 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 1 | 1325107 | 1594 | 73182 | 37403464 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 2 | 1576530 | 1660 | 71182 | 36376050 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 3 | 1607238 | 2119 | 71182 | 36376050 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 1 | 85527 | 435 | 103520 | 40404184 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 2 | 98175 | 475 | 103520 | 40404184 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 3 | 100358 | 353 | 103520 | 40404184 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 1 | 2268 | 204 | 11730 | 6187005 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 2 | 4277 | 99 | 11730 | 6187005 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 3 | 6965 | 424 | 11730 | 6187005 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 1 | 3989 | 151 | 14042 | 7619854 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 2 | 4365 | 370 | 14042 | 7619854 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 3 | 3897 | 362 | 14042 | 7619854 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 1 | 102 | 33 | 1210 | 180545 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 2 | 226 | 201 | 1210 | 180545 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 3 | 259 | 29 | 1210 | 180545 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 1 | 53 | 43 | 715 | 119146 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 2 | 35 | 27 | 715 | 119146 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 3 | 196 | 188 | 715 | 119146 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 1 | 1103 | 158 | 4435 | 1916701 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 2 | 585 | 72 | 4435 | 1916701 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 3 | 885 | 441 | 4435 | 1916701 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 1 | 141 | 131 | 270 | 119070 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 2 | 405 | 397 | 270 | 119070 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 3 | 351 | 340 | 270 | 119070 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 1 | 162 | 161 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 2 | 139 | 139 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 3 | 460 | 460 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 1 | 28 | 28 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 2 | 146 | 146 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 3 | 484 | 484 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |

## Доказательство записи логов приложения

| Метрика | Значение |
|---|---|
| Проверяемый файл логов | NO_LOG_PATH |
| Проверок логов | 48 |
| LOG OK | 0/48 |
| Новых байт в логах во время замеров | 0 |
| Правило доказательства | После HTTP-вызова endpoint-а файл логов должен увеличиться и новый фрагмент должен содержать один из MOEX-паттернов. |

| Evidence | Count |
|---|---|
| NO_LOG_PATH | 48 |


## Процесс после замеров

| Метрика | Значение |
|---|---|
| Working Set | 0 MB |
| Private | 0 MB |
| Peak Working Set | 0 MB |
| Handles |  |
| Threads | 0 |
| Total CPU | 0 сек |

