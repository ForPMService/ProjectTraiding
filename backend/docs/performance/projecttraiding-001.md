# Performance: projecttraiding-001

| Параметр | Значение |
|---|---|
| Дата | 2026-05-24 12:09:41 |
| OS | Майкрософт Windows 11 Домашняя |
| CPU | Intel(R) Core(TM) i9-9900KF CPU @ 3.60GHz |
| RAM | 31.9 GB |
| .NET | 10.0.300 |
| Git | `main` @ `c8f2e5b` |
| Endpoints | 27 |
| Итерации | 3 |
| Timeout | 120 сек |
| AppLogPath | NO_LOG_PATH |
| LogEvidenceWait | 3000 ms |

## ISS Reference

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetStockMarkets | 75 | 75 | 46 | 158 | 262 | 82 KB | 0 | 0/3 | NO_LOG_PATH |
| GetFuturesMarkets | 55 | 54 | 49 | 86 | 449 | 196 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK Candles

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetCandlesAsset | 150956 | 69 | 143723 | 151509 | 369377 | 54.1 MB | 0 | 0/3 | NO_LOG_PATH |
| GetCandlesFutures | 38124 | 222 | 37286 | 38996 | 75414 | 9.7 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK FUTOI

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetFutoi | 583 | 33 | 182 | 719 | 2194 | 555 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK SuperCandles EQ

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetSuperCandlesTradeStats | 58040 | 289 | 56840 | 58062 | 103694 | 42 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesOrderStats | 61178 | 335 | 61095 | 64020 | 104182 | 51.1 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesOrderBookStats | 52143 | 367 | 49798 | 53749 | 103520 | 38.5 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK SuperCandles FO

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetSuperCandlesFuturesTradeStats | 1944 | 136 | 1419 | 2338 | 11730 | 5.9 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesFuturesOrderBookStat | 2056 | 140 | 1220 | 2280 | 14042 | 7.3 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK HI2

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetHi2Asset | 55 | 30 | 39 | 70 | 1210 | 176 KB | 0 | 0/3 | NO_LOG_PATH |
| GetHi2Furure | 41 | 40 | 21 | 94 | 715 | 116 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK MegaAlerts

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetMegaAlerts | 610 | 167 | 332 | 1045 | 4435 | 1.8 MB | 0 | 0/3 | NO_LOG_PATH |
| GetMegaAlertsFutures | 34 | 34 | 31 | 53 | 270 | 116 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/offdays-all | 26 | 25 | 25 | 45 | 88 | 22 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/stock-offdays | 19 | 19 | 14 | 23 | 88 | 10 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/futures-offdays | 10 | 10 | 9 | 14 | 88 | 10 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Sessions

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/stock-session | 12 | 12 | 10 | 14 | 44 | 7 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/stock-session-types | 12 | 12 | 11 | 300 | 7 | 2 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/futures-session | 15 | 15 | 11 | 18 | 13 | 2 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/futures-session-types | 13 | 13 | 10 | 24 | 8 | 1 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Futures

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/forts-contracts | 57 | 57 | 57 | 170 | 449 | 212 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/options-series | 49 | 48 | 45 | 68 | 452 | 248 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Suspended

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/suspended-reasons | 37 | 37 | 28 | 105 | 28 | 10 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/suspended | 82205 | 104 | 81255 | 82734 | 187977 | 37.6 MB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Securities

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/security-attributes | 43 | 43 | 41 | 170 | 23 | 4 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/security-changes | 491 | 33 | 266 | 1168 | 7889 | 1.2 MB | 0 | 0/3 | NO_LOG_PATH |

## Детали по прогонам

| Endpoint | # | мс | TTFB мс | Строк | Байт | Mem до MB | Mem после MB | Дельта MB | Логи | Log bytes | Pattern | Ошибка |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| GetStockMarkets | 1 | 158 | 154 | 262 | 84189 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetStockMarkets | 2 | 75 | 75 | 262 | 84189 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetStockMarkets | 3 | 46 | 44 | 262 | 84189 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFuturesMarkets | 1 | 86 | 86 | 449 | 200206 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFuturesMarkets | 2 | 55 | 54 | 449 | 200206 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFuturesMarkets | 3 | 49 | 48 | 449 | 200206 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 1 | 151509 | 69 | 369377 | 56708303 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 2 | 150956 | 64 | 369377 | 56708303 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 3 | 143723 | 88 | 369377 | 56708303 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 1 | 38124 | 468 | 75414 | 10138466 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 2 | 37286 | 222 | 75414 | 10138466 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 3 | 38996 | 207 | 75414 | 10138466 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 1 | 182 | 47 | 2194 | 568479 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 2 | 583 | 32 | 2194 | 568479 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 3 | 719 | 33 | 2194 | 568479 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 1 | 56840 | 260 | 103694 | 44089257 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 2 | 58062 | 301 | 103694 | 44089257 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 3 | 58040 | 289 | 103694 | 44089257 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 1 | 61178 | 310 | 104182 | 53538583 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 2 | 61095 | 351 | 104182 | 53538583 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 3 | 64020 | 335 | 104182 | 53538583 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 1 | 53749 | 1067 | 103520 | 40404184 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 2 | 49798 | 367 | 103520 | 40404184 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 3 | 52143 | 307 | 103520 | 40404184 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 1 | 1944 | 136 | 11730 | 6187005 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 2 | 1419 | 74 | 11730 | 6187005 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 3 | 2338 | 153 | 11730 | 6187005 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 1 | 2280 | 133 | 14042 | 7619854 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 2 | 1220 | 166 | 14042 | 7619854 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 3 | 2056 | 140 | 14042 | 7619854 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 1 | 70 | 34 | 1210 | 180545 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 2 | 55 | 30 | 1210 | 180545 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 3 | 39 | 28 | 1210 | 180545 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 1 | 41 | 40 | 715 | 119146 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 2 | 21 | 21 | 715 | 119146 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 3 | 94 | 94 | 715 | 119146 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 1 | 610 | 167 | 4435 | 1916701 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 2 | 1045 | 730 | 4435 | 1916701 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 3 | 332 | 84 | 4435 | 1916701 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 1 | 53 | 53 | 270 | 119070 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 2 | 34 | 34 | 270 | 119070 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 3 | 31 | 31 | 270 | 119070 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/offdays-all | 1 | 45 | 45 | 88 | 22017 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/offdays-all | 2 | 26 | 25 | 88 | 22017 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/offdays-all | 3 | 25 | 25 | 88 | 22017 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 1 | 14 | 14 | 88 | 10529 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 2 | 23 | 22 | 88 | 10529 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 3 | 19 | 19 | 88 | 10529 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 1 | 14 | 14 | 88 | 10529 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 2 | 10 | 10 | 88 | 10529 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 3 | 9 | 9 | 88 | 10529 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session | 1 | 14 | 14 | 44 | 7455 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session | 2 | 10 | 10 | 44 | 7455 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session | 3 | 12 | 12 | 44 | 7455 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session-types | 1 | 12 | 12 | 7 | 1775 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session-types | 2 | 11 | 11 | 7 | 1775 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session-types | 3 | 300 | 300 | 7 | 1775 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session | 1 | 18 | 18 | 13 | 2374 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session | 2 | 15 | 15 | 13 | 2374 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session | 3 | 11 | 11 | 13 | 2374 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session-types | 1 | 10 | 10 | 8 | 1483 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session-types | 2 | 24 | 24 | 8 | 1483 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session-types | 3 | 13 | 13 | 8 | 1483 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/forts-contracts | 1 | 170 | 169 | 449 | 217278 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/forts-contracts | 2 | 57 | 57 | 449 | 217278 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/forts-contracts | 3 | 57 | 56 | 449 | 217278 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/options-series | 1 | 49 | 48 | 452 | 254095 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/options-series | 2 | 68 | 67 | 452 | 254095 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/options-series | 3 | 45 | 44 | 452 | 254095 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended-reasons | 1 | 105 | 105 | 28 | 9929 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended-reasons | 2 | 28 | 27 | 28 | 9929 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended-reasons | 3 | 37 | 37 | 28 | 9929 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended | 1 | 82734 | 31 | 187977 | 39435111 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended | 2 | 81255 | 104 | 187977 | 39435111 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended | 3 | 82205 | 145 | 187977 | 39435111 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-attributes | 1 | 170 | 170 | 23 | 4253 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-attributes | 2 | 41 | 41 | 23 | 4253 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-attributes | 3 | 43 | 43 | 23 | 4253 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-changes | 1 | 1168 | 43 | 7889 | 1244337 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-changes | 2 | 266 | 33 | 7889 | 1244337 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-changes | 3 | 491 | 30 | 7889 | 1244337 | 54.9 | 54.9 | 0 | NO_LOG_PATH | 0 |  | - |

## Доказательство записи логов приложения

| Метрика | Значение |
|---|---|
| Проверяемый файл логов | NO_LOG_PATH |
| Проверок логов | 81 |
| LOG OK | 0/81 |
| Новых байт в логах во время замеров | 0 |
| Правило доказательства | После HTTP-вызова endpoint-а файл логов должен увеличиться и новый фрагмент должен содержать один из MOEX-паттернов. |

| Evidence | Count |
|---|---|
| NO_LOG_PATH | 81 |


## Процесс после замеров

| Метрика | Значение |
|---|---|
| Working Set | 54.9 MB |
| Private | 25.6 MB |
| Peak Working Set | 72.5 MB |
| Handles | 342 |
| Threads | 10 |
| Total CPU | 2.44 сек |

