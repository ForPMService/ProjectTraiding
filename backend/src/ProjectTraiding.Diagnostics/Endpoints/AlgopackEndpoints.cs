using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Diagnostics.Contracts;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using System.Text.Json.Serialization.Metadata;


namespace ProjectTraiding.Diagnostics.Endpoints
{
    /// <summary>
    /// Diagnostic raw/parsed endpoint-ы ALGOPACK для ручной проверки рыночных контрактов.
    /// </summary>
    public static class AlgopackEndpoints
    {
        private const string DiagnosticSource = "MOEX_ALGOPACK";

        public static IEndpointRouteBuilder MapAlgopackEndpoints(this IEndpointRouteBuilder routes)
        {
            RouteGroupBuilder diagnosticsGroup = routes.MapGroup("/moex");

            diagnosticsGroup.MapGet("/raw/market/stock/candles/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? interval,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "candles", "stock", ticker, validationMessage);
                }

                string method = $"/engines/stock/markets/shares/boards/tqbr/securities/{ticker}/candles.json";
                Dictionary<string, string> queryParams = CreateRawCandlesQuery(from!, till!, interval ?? 1);
                return await ExecuteRawAsync("candles", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/stock/candles/{ticker}", GetParsedStockCandlesAsync);

            diagnosticsGroup.MapGet("/raw/market/futures/candles/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? interval,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "candles", "futures", ticker, validationMessage);
                }

                string method = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/candles.json";
                Dictionary<string, string> queryParams = CreateRawCandlesQuery(from!, till!, interval ?? 1);
                return await ExecuteRawAsync("candles", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/futures/candles/{ticker}", GetParsedFuturesCandlesAsync);

            diagnosticsGroup.MapGet("/raw/market/stock/tradestats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "tradestats", "stock", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/eq/tradestats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.AlgCandlesTradeStatSchema.BuildColumnsParam());
                return await ExecuteRawAsync("tradestats", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/stock/tradestats/{ticker}", GetParsedStockTradeStatsAsync);

            diagnosticsGroup.MapGet("/raw/market/futures/tradestats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "tradestats", "futures", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/fo/tradestats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.FuturesTradeStatsSchema.BuildColumnsParam());
                return await ExecuteRawAsync("tradestats", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/futures/tradestats/{ticker}", GetParsedFuturesTradeStatsAsync);

            diagnosticsGroup.MapGet("/raw/market/stock/obstats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "obstats", "stock", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/eq/obstats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.AlgOrderBookStats5mSchema.BuildColumnsParam());
                return await ExecuteRawAsync("obstats", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/stock/obstats/{ticker}", GetParsedStockOrderBookStatsAsync);

            diagnosticsGroup.MapGet("/raw/market/futures/obstats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "obstats", "futures", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/fo/obstats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.AlgFuturesOrderBookSchema.BuildColumnsParam());
                return await ExecuteRawAsync("obstats", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/futures/obstats/{ticker}", GetParsedFuturesOrderBookStatsAsync);

            diagnosticsGroup.MapGet("/raw/market/stock/orderstats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "orderstats", "stock", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/eq/orderstats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.AlgOrderStats5mSchema.BuildColumnsParam());
                return await ExecuteRawAsync("orderstats", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/stock/orderstats/{ticker}", GetParsedStockOrderStatsAsync);

            diagnosticsGroup.MapGet("/raw/market/futures/futoi/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "futoi", "futures", ticker, validationMessage);
                }

                string method = $"/analyticalproducts/futoi/securities/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawFutoiQuery(from!, till!);
                return await ExecuteRawAsync("futoi", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/futures/futoi/{ticker}", GetParsedFuturesFutoiAsync);

            diagnosticsGroup.MapGet("/raw/market/stock/hi2/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "hi2", "stock", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/eq/hi2/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.Hi2AssetSchema.BuildColumnsParam());
                return await ExecuteRawAsync("hi2", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/stock/hi2/{ticker}", GetParsedStockHi2Async);

            diagnosticsGroup.MapGet("/raw/market/futures/hi2/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "hi2", "futures", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/fo/hi2/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.Hi2FuturesSchema.BuildColumnsParam());
                return await ExecuteRawAsync("hi2", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/futures/hi2/{ticker}", GetParsedFuturesHi2Async);

            diagnosticsGroup.MapGet("/raw/market/stock/alerts/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "alerts", "stock", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/eq/alerts/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.MegaAlertsAssetSchema.BuildColumnsParam());
                return await ExecuteRawAsync("alerts", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/stock/alerts/{ticker}", GetParsedStockAlertsAsync);

            diagnosticsGroup.MapGet("/raw/market/futures/alerts/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                if (validationMessage is not null)
                {
                    return CreateDiagnosticError(400, "alerts", "futures", ticker, validationMessage);
                }

                string method = $"/datashop/algopack/fo/alerts/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, ProjectTraiding.Moex.Parsing.ColumnAndNumbersForParsing.MegaAlertsFuturesSchema.BuildColumnsParam());
                return await ExecuteRawAsync("alerts", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/parsed/market/futures/alerts/{ticker}", GetParsedFuturesAlertsAsync);

            return routes;
        }

        private static Task<IResult> GetParsedStockCandlesAsync(
            string ticker,
            string? from,
            string? till,
            int? interval,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "candles", "stock", ticker, validationMessage));
            }

            string method = $"/engines/stock/markets/shares/boards/tqbr/securities/{ticker}/candles.json";
            int effectiveInterval = interval ?? 1;
            return ExecuteParsedAsync(
                "candles",
                "stock",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetCandles(
                        method,
                        CreateParsedCandlesQuery(from!, till!, effectiveInterval),
                        telemetryMarket: MoexMarkets.Stock,
                        cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListCandlesDTO,
                ct);
        }

        private static Task<IResult> GetParsedFuturesCandlesAsync(
            string ticker,
            string? from,
            string? till,
            int? interval,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "candles", "futures", ticker, validationMessage));
            }

            string method = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/candles.json";
            int effectiveInterval = interval ?? 1;
            return ExecuteParsedAsync(
                "candles",
                "futures",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetCandles(
                        method,
                        CreateParsedCandlesQuery(from!, till!, effectiveInterval),
                        telemetryMarket: MoexMarkets.Futures,
                        cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListCandlesDTO,
                ct);
        }

        private static Task<IResult> GetParsedStockTradeStatsAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "tradestats", "stock", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/eq/tradestats/{ticker}.json";
            return ExecuteParsedAsync(
                "tradestats",
                "stock",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetSuperCandlesTradeStats5m(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListSuperCandlesTradeStats5mDTO,
                ct);
        }

        private static Task<IResult> GetParsedFuturesTradeStatsAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "tradestats", "futures", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/fo/tradestats/{ticker}.json";
            return ExecuteParsedAsync(
                "tradestats",
                "futures",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetSuperCandlesFuturesTradeStats5m(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListSuperCandlesFuturesTradeStats5mDTO,
                ct);
        }

        private static Task<IResult> GetParsedStockOrderBookStatsAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "obstats", "stock", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/eq/obstats/{ticker}.json";
            return ExecuteParsedAsync(
                "obstats",
                "stock",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetSuperCandlesOrderBookStats5m(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListSuperCandlesOrderBookStats5mDTO,
                ct);
        }

        private static Task<IResult> GetParsedFuturesOrderBookStatsAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "obstats", "futures", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/fo/obstats/{ticker}.json";
            return ExecuteParsedAsync(
                "obstats",
                "futures",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetSuperCandlesFuturesOrderBookStats5m(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListSuperCandlesFuturesOrderBookStats5mDTO,
                ct);
        }

        private static Task<IResult> GetParsedStockOrderStatsAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "orderstats", "stock", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/eq/orderstats/{ticker}.json";
            return ExecuteParsedAsync(
                "orderstats",
                "stock",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetSuperCandlesOrderStats5m(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListSuperCandlesOrderStats5mDTO,
                ct);
        }

        private static Task<IResult> GetParsedFuturesFutoiAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "futoi", "futures", ticker, validationMessage));
            }

            string method = $"/analyticalproducts/futoi/securities/{ticker}.json";
            return ExecuteParsedAsync(
                "futoi",
                "futures",
                ticker,
                cancellationToken => CollectAsync(
                    client.StreamFutoi(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListFutoiDTO,
                ct);
        }

        private static Task<IResult> GetParsedStockHi2Async(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "hi2", "stock", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/eq/hi2/{ticker}.json";
            return ExecuteParsedAsync(
                "hi2",
                "stock",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetHi2Asset5m(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListHi2AssetDTO,
                ct);
        }

        private static Task<IResult> GetParsedFuturesHi2Async(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "hi2", "futures", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/fo/hi2/{ticker}.json";
            return ExecuteParsedAsync(
                "hi2",
                "futures",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetHi2Futures5m(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListHi2FuturesDTO,
                ct);
        }

        private static Task<IResult> GetParsedStockAlertsAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "alerts", "stock", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/eq/alerts/{ticker}.json";
            return ExecuteParsedAsync(
                "alerts",
                "stock",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetMegaAlerts(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListMegaAlertsAssetsDTO,
                ct);
        }

        private static Task<IResult> GetParsedFuturesAlertsAsync(
            string ticker,
            string? from,
            string? till,
            MoexHttpAlgClient client,
            CancellationToken ct)
        {
            string? validationMessage = ValidateRangeRequest(ticker, from, till);
            if (validationMessage is not null)
            {
                return Task.FromResult(CreateDiagnosticError(400, "alerts", "futures", ticker, validationMessage));
            }

            string method = $"/datashop/algopack/fo/alerts/{ticker}.json";
            return ExecuteParsedAsync(
                "alerts",
                "futures",
                ticker,
                cancellationToken => CollectAsync(
                    client.GetMegaAlertsFutures(method, CreateParsedRangeQuery(from!, till!), cancellationToken: cancellationToken),
                    cancellationToken),
                DiagnosticsJsonContext.Default.ListMegaAlertsFuturesDTO,
                ct);
        }

        private static async Task<IResult> ExecuteRawAsync(
            string kind,
            string market,
            string ticker,
            MoexHttpAlgClient client,
            string method,
            Dictionary<string, string> queryParams,
            CancellationToken ct)
        {
            try
            {
                string raw = await client.GetRaw(method, queryParams, ct);
                return Results.Text(raw, "application/json");
            }
            catch (Exception ex)
            {
                return CreateDiagnosticError(500, kind, market, ticker, ex.Message);
            }
        }

        private static async Task<IResult> ExecuteParsedAsync<T>(
            string kind,
            string market,
            string ticker,
            Func<CancellationToken, Task<List<T>>> loader,
            JsonTypeInfo<List<T>> typeInfo,
            CancellationToken ct)
        {
            try
            {
                List<T> items = await loader(ct);
                return Results.Json(items, typeInfo);
            }
            catch (Exception ex)
            {
                return CreateDiagnosticError(500, kind, market, ticker, ex.Message);
            }
        }

        private static async Task<List<T>> CollectAsync<T>(
            IAsyncEnumerable<List<T>> pages,
            CancellationToken ct)
        {
            List<T> items = new List<T>();

            await foreach (List<T> page in pages.WithCancellation(ct))
            {
                items.AddRange(page);
            }

            return items;
        }

        private static Dictionary<string, string> CreateParsedRangeQuery(string from, string till)
        {
            return new Dictionary<string, string>
            {
                ["from"] = from,
                ["till"] = till
            };
        }

        private static Dictionary<string, string> CreateParsedCandlesQuery(string from, string till, int interval)
        {
            return new Dictionary<string, string>
            {
                ["interval"] = interval.ToString(),
                ["from"] = from,
                ["till"] = till
            };
        }

        private static Dictionary<string, string> CreateRawCandlesQuery(string from, string till, int interval)
        {
            return new Dictionary<string, string>
            {
                ["interval"] = interval.ToString(),
                ["from"] = from,
                ["till"] = till,
                ["iss.meta"] = "off",
                ["iss.only"] = "candles"
            };
        }

        private static Dictionary<string, string> CreateRawDataQuery(string from, string till, string columns)
        {
            return new Dictionary<string, string>
            {
                ["from"] = from,
                ["till"] = till,
                ["iss.meta"] = "off",
                ["iss.only"] = "data,data.cursor",
                ["data.columns"] = columns
            };
        }

        private static Dictionary<string, string> CreateRawFutoiQuery(string from, string till)
        {
            return new Dictionary<string, string>
            {
                ["from"] = from,
                ["till"] = till,
                ["iss.meta"] = "off"
            };
        }

        private static string? ValidateRangeRequest(string ticker, string? from, string? till)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                return "Route parameter ticker is required.";
            }

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(till))
            {
                return "Query parameters from and till are required.";
            }

            return null;
        }

        private static IResult CreateDiagnosticError(int statusCode, string kind, string market, string ticker, string message)
        {
            string json = "{"
                + "\"status\":\"error\"," 
                + "\"source\":\"" + EscapeJson(DiagnosticSource) + "\"," 
                + "\"kind\":\"" + EscapeJson(kind) + "\"," 
                + "\"market\":\"" + EscapeJson(market) + "\"," 
                + "\"ticker\":\"" + EscapeJson(ticker) + "\"," 
                + "\"message\":\"" + EscapeJson(message) + "\""
                + "}";

            return Results.Text(json, "application/json", statusCode: statusCode);
        }

        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

    }
}    

