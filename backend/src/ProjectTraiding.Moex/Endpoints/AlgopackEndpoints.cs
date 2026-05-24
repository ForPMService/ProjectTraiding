using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System.Runtime.CompilerServices;


namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Source endpoint-ы MOEX: в момент запроса идут в MOEX,
    /// парсят ответ и возвращают DTO MOEX.
    /// Это не ручки витрины для фронта; ручки витрины появятся позже
    /// и будут читать данные из PostgreSQL/ClickHouse.
    /// </summary>
    public static class AlgopackEndpoints
    {
        public static IEndpointRouteBuilder MapAlgopackEndpoints(this IEndpointRouteBuilder routes)
        {
            // === Фьючерсы ===
            routes.MapGet("/GetSuperCandlesFuturesTradeStats", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/fo/tradestats/SiM6.json";

                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2026-01-28",
                    ["till"] = "2026-05-05"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSuperCandlesFuturesTradeStats", MoexLogSources.Algopack, url, "from=2026-01-28&till=2026-05-05");
                return StreamFuturesTradeStats(moexHttpAlgClient, url, queryParams, ct);
            });

            routes.MapGet("/GetSuperCandlesFuturesOrderBookStat", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/fo/obstats/SiM6.json";

                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2026-01-28",
                    ["till"] = "2026-04-30"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSuperCandlesFuturesOrderBookStat", MoexLogSources.Algopack, url, "from=2026-01-28&till=2026-04-30");
                return StreamFuturesOrderBookStats(moexHttpAlgClient, url, queryParams, ct);
            });


            // === FUTOI ===
            routes.MapGet("/GetFutoi", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/analyticalproducts/futoi/securities/Si.json";

                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2026-04-29",
                    ["till"] = "2026-05-05"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetFutoi", MoexLogSources.Algopack, url, "from=2026-04-29&till=2026-05-05");
                return StreamFutoiItems(moexHttpAlgClient, url, queryParams, ct);
            });

            // === HI2 ===
            routes.MapGet("/GetHi2Asset", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/eq/hi2/SBER.json";

                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2026-01-03",
                    ["till"] = "2026-05-03"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetHi2Asset", MoexLogSources.Algopack, url, "from=2026-01-03&till=2026-05-03");
                return StreamHi2Asset(moexHttpAlgClient, url, queryParams, ct);
            });

            routes.MapGet("/GetHi2Furure", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/fo/hi2/SiM6.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2026-01-30",
                    ["till"] = "2026-05-04"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetHi2Furure", MoexLogSources.Algopack, url, "from=2026-01-30&till=2026-05-04");
                return StreamHi2Futures(moexHttpAlgClient, url, queryParams, ct);
            });

            // === Мега-оповещения ===
            routes.MapGet("/GetMegaAlerts", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/eq/alerts/SBER.json";

                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2024-04-28",
                    ["till"] = "2026-04-30"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetMegaAlerts", MoexLogSources.Algopack, url, "from=2024-04-28&till=2026-04-30");
                return StreamMegaAlerts(moexHttpAlgClient, url, queryParams, ct);
            });

            routes.MapGet("/GetMegaAlertsFutures", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/fo/alerts/SiM6.json";

                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2026-01-28",
                    ["till"] = "2026-04-30"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetMegaAlertsFutures", MoexLogSources.Algopack, url, "from=2026-01-28&till=2026-04-30");
                return StreamMegaAlertsFutures(moexHttpAlgClient, url, queryParams, ct);
            });


            routes.MapGet("/GetSuperCandlesTradeStats", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/eq/tradestats/SMLT.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2024-04-08",
                    ["till"] = "2026-04-17"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSuperCandlesTradeStats", MoexLogSources.Algopack, url, "from=2024-04-08&till=2026-04-17");
                return StreamTradeStats(moexHttpAlgClient, url, queryParams, ct);
            });
            routes.MapGet("/GetSuperCandlesOrderStats", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/eq/orderstats/SMLT.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2024-04-08",
                    ["till"] = "2026-04-17"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSuperCandlesOrderStats", MoexLogSources.Algopack, url, "from=2024-04-08&till=2026-04-17");
                return StreamOrderStats(moexHttpAlgClient, url, queryParams, ct);
            });
            routes.MapGet("/GetSuperCandlesOrderBookStats", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/datashop/algopack/eq/obstats/SMLT.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["from"] = "2024-04-08",
                    ["till"] = "2026-04-17"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetSuperCandlesOrderBookStats", MoexLogSources.Algopack, url, "from=2024-04-08&till=2026-04-17");
                return StreamOrderBookStats(moexHttpAlgClient, url, queryParams, ct);
            });

            routes.MapGet("/GetCandlesAsset", (
                MoexHttpAlgClient moexHttpAlgClient,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                string url = "/engines/stock/markets/shares/boards/tqbr/securities/SBER/candles.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["interval"] = "1",
                    ["from"] = "2025-01-28",
                    ["till"] = "2026-05-05"
                };

                var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
                MoexLogMessages.LoadStarted(logger, "GetCandlesAsset", MoexLogSources.Algopack, url, "interval=1&from=2025-01-28&till=2026-05-05");
                return StreamCandles(moexHttpAlgClient, url, queryParams, ct);
            });




            routes.MapGet("/GetCandlesFutures", (
               MoexHttpAlgClient moexHttpAlgClient,
               ILoggerFactory loggerFactory,
               CancellationToken ct) =>
           {
               string url = "/engines/futures/markets/forts/boards/RFUD/securities/SiM6/candles.json";
               Dictionary<string, string> queryParams = new Dictionary<string, string>
               {
                   ["interval"] = "1",
                   ["from"] = "2025-01-28",
                   ["till"] = "2026-05-05"
               };

               var logger = loggerFactory.CreateLogger("AlgopackEndpoints");
               MoexLogMessages.LoadStarted(logger, "GetCandlesFutures", MoexLogSources.Algopack, url, "interval=1&from=2025-01-28&till=2026-05-05");
               return StreamCandles(moexHttpAlgClient, url, queryParams, ct);
           });
            return routes;
        }
        public static async IAsyncEnumerable<CandlesDTO> StreamCandles(MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<CandlesDTO> candlesBatch in client.GetCandles(url, queryParams, ct))
            {
                foreach (var candle in candlesBatch)
                {
                    yield return candle;
                }
            }
        }

        static async IAsyncEnumerable<SuperCandlesTradeStats5mDTO> StreamTradeStats(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<SuperCandlesTradeStats5mDTO> batch in client.GetSuperCandlesTradeStats5m(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<SuperCandlesOrderStats5mDTO> StreamOrderStats(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<SuperCandlesOrderStats5mDTO> batch in client.GetSuperCandlesOrderStats5m(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<SuperCandlesOrderBookStats5mDTO> StreamOrderBookStats(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<SuperCandlesOrderBookStats5mDTO> batch in client.GetSuperCandlesOrderBookStats5m(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<SuperCandlesFuturesTradeStats5mDTO> StreamFuturesTradeStats(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<SuperCandlesFuturesTradeStats5mDTO> batch in client.GetSuperCandlesFuturesTradeStats5m(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<SuperCandlesFuturesOrderBookStats5mDTO> StreamFuturesOrderBookStats(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<SuperCandlesFuturesOrderBookStats5mDTO> batch in client.GetSuperCandlesFuturesOrderBookStats5m(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<FutoiDTO> StreamFutoiItems(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<FutoiDTO> batch in client.StreamFutoi(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<Hi2AssetDTO> StreamHi2Asset(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<Hi2AssetDTO> batch in client.GetHi2Asset5m(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<Hi2FuturesDTO> StreamHi2Futures(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<Hi2FuturesDTO> batch in client.GetHi2Furures5m(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<MegaAlertsAssetsDTO> StreamMegaAlerts(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<MegaAlertsAssetsDTO> batch in client.GetMegaAlerts(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

        static async IAsyncEnumerable<MegaAlertsFuturesDTO> StreamMegaAlertsFutures(
            MoexHttpAlgClient client, string url, Dictionary<string, string> queryParams,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (List<MegaAlertsFuturesDTO> batch in client.GetMegaAlertsFutures(url, queryParams, ct))
            {
                foreach (var item in batch)
                {
                    yield return item;
                }
            }
        }

    }
}    

