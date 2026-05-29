# Parallel Benchmark: parallel-001

| Параметр | Значение |
|---|---|
| Дата | 2026-05-29 16:50:00 |
| Режим | Параллельный (все 27 endpoint-ов одновременно) |
| Итерации | 3 |
| Timeout | 300 сек |

## Итерации

| # | Время сек | OK | FAIL | API endpoint/s |
|---|---|---|---|---|
| 1 | 212.1 | 27 | 0 | ~0.1 |
| 2 | 228.7 | 27 | 0 | ~0.1 |
| 3 | 224.2 | 27 | 0 | ~0.1 |

## Endpoint-ы (медиана по итерациям)

| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Байт | Ошибки |
|---|---|---|---|---|---|---|
| GetStockMarkets | 1982 | 1982 | 150 | 2568 | 82 KB | - |
| GetFuturesMarkets | 1535 | 1534 | 420 | 2539 | 193 KB | - |
| GetCandlesAsset | 224104 | 384 | 211982 | 228554 | 54.1 MB | - |
| GetCandlesFutures | 66107 | 478 | 65230 | 68804 | 9.7 MB | - |
| GetFutoi | 2426 | 320 | 1697 | 6332 | 555 KB | - |
| GetSuperCandlesTradeStats | 100214 | 1907 | 96156 | 104731 | 42 MB | - |
| GetSuperCandlesOrderStats | 102561 | 840 | 102001 | 113362 | 51.1 MB | - |
| GetSuperCandlesOrderBookStats | 90401 | 685 | 83918 | 92069 | 38.5 MB | - |
| GetSuperCandlesFuturesTradeStats | 6750 | 530 | 4047 | 8782 | 5.9 MB | - |
| GetSuperCandlesFuturesOrderBookStat | 4126 | 229 | 3907 | 4595 | 7.3 MB | - |
| GetHi2Asset | 164 | 53 | 76 | 418 | 176 KB | - |
| GetHi2Furure | 320 | 310 | 104 | 568 | 116 KB | - |
| GetMegaAlerts | 1140 | 183 | 1105 | 1231 | 1.8 MB | - |
| GetMegaAlertsFutures | 85 | 77 | 85 | 891 | 116 KB | - |
| calendar/offdays-all | 153 | 153 | 95 | 499 | 22 KB | - |
| calendar/stock-offdays | 79 | 78 | 44 | 524 | 10 KB | - |
| calendar/futures-offdays | 30 | 30 | 28 | 136 | 10 KB | - |
| calendar/stock-session | 42 | 41 | 34 | 260 | 89 KB | - |
| calendar/stock-session-types | 40 | 40 | 28 | 113 | 2 KB | - |
| calendar/futures-session | 128 | 128 | 24 | 639 | 1 KB | - |
| calendar/futures-session-types | 147 | 147 | 19 | 702 | 1 KB | - |
| calendar/forts-contracts | 157 | 156 | 147 | 530 | 210 KB | - |
| calendar/options-series | 403 | 402 | 47 | 513 | 247 KB | - |
| calendar/suspended-reasons | 237 | 237 | 184 | 283 | 10 KB | - |
| calendar/suspended | 107067 | 81 | 106426 | 112702 | 39.2 MB | - |
| calendar/security-attributes | 647 | 647 | 462 | 695 | 4 KB | - |
| calendar/security-changes | 3747 | 47 | 3025 | 3759 | 1.2 MB | - |


