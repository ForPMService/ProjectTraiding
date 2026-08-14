using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;
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

            // Колонки сырых точек курсорных видов берутся из декларации реестра.
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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.TradeStatsStock.BuildColumnsParam());
                return await ExecuteRawAsync("tradestats", "stock", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.TradeStatsFutures.BuildColumnsParam());
                return await ExecuteRawAsync("tradestats", "futures", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.ObStatsStock.BuildColumnsParam());
                return await ExecuteRawAsync("obstats", "stock", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.ObStatsFutures.BuildColumnsParam());
                return await ExecuteRawAsync("obstats", "futures", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.OrderStatsStock.BuildColumnsParam());
                return await ExecuteRawAsync("orderstats", "stock", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.Hi2Stock.BuildColumnsParam());
                return await ExecuteRawAsync("hi2", "stock", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.Hi2Futures.BuildColumnsParam());
                return await ExecuteRawAsync("hi2", "futures", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.MegaAlertsStock.BuildColumnsParam());
                return await ExecuteRawAsync("alerts", "stock", ticker, client, method, queryParams, ct);
            });

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
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.MegaAlertsFutures.BuildColumnsParam());
                return await ExecuteRawAsync("alerts", "futures", ticker, client, method, queryParams, ct);
            });

            return routes;
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

