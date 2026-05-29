# Performance: projecttraiding-002

| Параметр | Значение |
|---|---|
| Дата | 2026-05-29 10:45:45 |
| OS | Майкрософт Windows 11 Домашняя |
| CPU | Intel(R) Core(TM) i9-9900KF CPU @ 3.60GHz |
| RAM | 31.9 GB |
| .NET | 10.0.300 |
| Git | `main` @ `3d7f3fd` |
| Endpoints | 27 |
| Итерации | 3 |
| Timeout | 120 сек |
| AppLogPath | NO_LOG_PATH |
| LogEvidenceWait | 3000 ms |

## ISS Reference

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetStockMarkets | 46 | 46 | 45 | 143 | 262 | 82 KB | 0 | 0/3 | NO_LOG_PATH |
| GetFuturesMarkets | 70 | 67 | 61 | 101 | 444 | 193 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK Candles

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetCandlesAsset | 207737 | 706 | 176432 | 241846 | 369377 | 54.1 MB | 0 | 0/3 | NO_LOG_PATH |
| GetCandlesFutures | 25005 | 111 | 24871 | 31483 | 75414 | 9.7 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK FUTOI

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetFutoi | 343 | 47 | 223 | 594 | 2194 | 555 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK SuperCandles EQ

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetSuperCandlesTradeStats | 67807 | 451 | 64377 | 76115 | 103694 | 42 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesOrderStats | 67923 | 484 | 67527 | 71691 | 104182 | 51.1 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesOrderBookStats | 59697 | 509 | 55645 | 61896 | 103520 | 38.5 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK SuperCandles FO

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetSuperCandlesFuturesTradeStats | 1855 | 149 | 1454 | 2093 | 11730 | 5.9 MB | 0 | 0/3 | NO_LOG_PATH |
| GetSuperCandlesFuturesOrderBookStat | 2313 | 157 | 1666 | 4647 | 14042 | 7.3 MB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK HI2

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetHi2Asset | 91 | 74 | 74 | 126 | 1210 | 176 KB | 0 | 0/3 | NO_LOG_PATH |
| GetHi2Furure | 27 | 27 | 26 | 55 | 715 | 116 KB | 0 | 0/3 | NO_LOG_PATH |

## ALGOPACK MegaAlerts

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| GetMegaAlerts | 427 | 136 | 365 | 949 | 4435 | 1.8 MB | 0 | 0/3 | NO_LOG_PATH |
| GetMegaAlertsFutures | 45 | 45 | 41 | 62 | 270 | 116 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/offdays-all | 24 | 24 | 17 | 129 | 88 | 22 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/stock-offdays | 41 | 41 | 36 | 46 | 88 | 10 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/futures-offdays | 27 | 27 | 18 | 43 | 88 | 10 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Sessions

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/stock-session | 51 | 50 | 31 | 58 | 516 | 89 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/stock-session-types | 23 | 23 | 17 | 33 | 7 | 2 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/futures-session | 32 | 32 | 16 | 47 | 7 | 1 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/futures-session-types | 15 | 15 | 15 | 39 | 8 | 1 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Futures

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/forts-contracts | 156 | 155 | 39 | 365 | 444 | 210 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/options-series | 58 | 57 | 41 | 101 | 450 | 247 KB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Suspended

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/suspended-reasons | 34 | 34 | 25 | 130 | 28 | 10 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/suspended | 102468 | 133 | 100988 | 105787 | 195772 | 39.2 MB | 0 | 0/3 | NO_LOG_PATH |

## Calendar Securities

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |
|---|---|---|---|---|---|---|---|---|---|
| calendar/security-attributes | 225 | 225 | 60 | 355 | 23 | 4 KB | 0 | 0/3 | NO_LOG_PATH |
| calendar/security-changes | 656 | 45 | 259 | 1759 | 8250 | 1.2 MB | 0 | 0/3 | NO_LOG_PATH |

## Детали по прогонам

| Endpoint | # | мс | TTFB мс | Строк | Байт | Mem до MB | Mem после MB | Дельта MB | Логи | Log bytes | Pattern | Ошибка |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| GetStockMarkets | 1 | 143 | 140 | 262 | 84176 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetStockMarkets | 2 | 46 | 46 | 262 | 84176 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetStockMarkets | 3 | 45 | 44 | 262 | 84176 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFuturesMarkets | 1 | 70 | 67 | 444 | 198093 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFuturesMarkets | 2 | 101 | 100 | 444 | 198093 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFuturesMarkets | 3 | 61 | 60 | 444 | 198093 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 1 | 176432 | 706 | 369377 | 56708303 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 2 | 207737 | 82 | 369377 | 56708303 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesAsset | 3 | 241846 | 3214 | 369377 | 56708303 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 1 | 31483 | 111 | 75414 | 10138466 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 2 | 25005 | 45 | 75414 | 10138466 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetCandlesFutures | 3 | 24871 | 244 | 75414 | 10138466 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 1 | 223 | 47 | 2194 | 568479 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 2 | 343 | 72 | 2194 | 568479 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetFutoi | 3 | 594 | 31 | 2194 | 568479 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 1 | 67807 | 259 | 103694 | 44089257 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 2 | 76115 | 451 | 103694 | 44089257 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesTradeStats | 3 | 64377 | 677 | 103694 | 44089257 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 1 | 67527 | 484 | 104182 | 53538583 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 2 | 71691 | 443 | 104182 | 53538583 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderStats | 3 | 67923 | 486 | 104182 | 53538583 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 1 | 61896 | 509 | 103520 | 40404184 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 2 | 55645 | 520 | 103520 | 40404184 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesOrderBookStats | 3 | 59697 | 403 | 103520 | 40404184 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 1 | 1855 | 198 | 11730 | 6187005 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 2 | 1454 | 145 | 11730 | 6187005 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesTradeStats | 3 | 2093 | 149 | 11730 | 6187005 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 1 | 4647 | 975 | 14042 | 7619854 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 2 | 1666 | 125 | 14042 | 7619854 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetSuperCandlesFuturesOrderBookStat | 3 | 2313 | 157 | 14042 | 7619854 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 1 | 126 | 84 | 1210 | 180545 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 2 | 74 | 41 | 1210 | 180545 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Asset | 3 | 91 | 74 | 1210 | 180545 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 1 | 55 | 55 | 715 | 119146 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 2 | 27 | 27 | 715 | 119146 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetHi2Furure | 3 | 26 | 26 | 715 | 119146 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 1 | 949 | 176 | 4435 | 1916701 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 2 | 365 | 136 | 4435 | 1916701 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlerts | 3 | 427 | 98 | 4435 | 1916701 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 1 | 41 | 40 | 270 | 119070 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 2 | 45 | 45 | 270 | 119070 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| GetMegaAlertsFutures | 3 | 62 | 62 | 270 | 119070 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/offdays-all | 1 | 129 | 129 | 88 | 22017 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/offdays-all | 2 | 24 | 24 | 88 | 22017 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/offdays-all | 3 | 17 | 16 | 88 | 22017 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 1 | 36 | 36 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 2 | 46 | 46 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-offdays | 3 | 41 | 41 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 1 | 43 | 43 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 2 | 27 | 27 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-offdays | 3 | 18 | 17 | 88 | 10529 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session | 1 | 51 | 50 | 516 | 90847 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session | 2 | 58 | 58 | 516 | 90847 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session | 3 | 31 | 30 | 516 | 90847 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session-types | 1 | 23 | 23 | 7 | 1775 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session-types | 2 | 17 | 17 | 7 | 1775 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/stock-session-types | 3 | 33 | 33 | 7 | 1775 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session | 1 | 16 | 16 | 7 | 1266 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session | 2 | 47 | 46 | 7 | 1266 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session | 3 | 32 | 32 | 7 | 1266 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session-types | 1 | 15 | 14 | 8 | 1483 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session-types | 2 | 39 | 38 | 8 | 1483 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/futures-session-types | 3 | 15 | 15 | 8 | 1483 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/forts-contracts | 1 | 156 | 155 | 444 | 215140 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/forts-contracts | 2 | 39 | 39 | 444 | 215140 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/forts-contracts | 3 | 365 | 364 | 444 | 215140 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/options-series | 1 | 41 | 40 | 450 | 252805 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/options-series | 2 | 58 | 57 | 450 | 252805 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/options-series | 3 | 101 | 100 | 450 | 252805 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended-reasons | 1 | 130 | 130 | 28 | 9929 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended-reasons | 2 | 25 | 25 | 28 | 9929 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended-reasons | 3 | 34 | 34 | 28 | 9929 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended | 1 | 102468 | 41 | 195772 | 41060919 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended | 2 | 105787 | 147 | 195772 | 41060919 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/suspended | 3 | 100988 | 133 | 195772 | 41060919 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-attributes | 1 | 355 | 355 | 23 | 4253 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-attributes | 2 | 60 | 60 | 23 | 4253 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-attributes | 3 | 225 | 225 | 23 | 4253 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-changes | 1 | 1759 | 49 | 8250 | 1302579 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-changes | 2 | 259 | 45 | 8250 | 1302579 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |
| calendar/security-changes | 3 | 656 | 32 | 8250 | 1302579 | 0 | 0 | 0 | NO_LOG_PATH | 0 |  | - |

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
| Working Set | 0 MB |
| Private | 0 MB |
| Peak Working Set | 0 MB |
| Handles |  |
| Threads | 0 |
| Total CPU | 0 сек |

