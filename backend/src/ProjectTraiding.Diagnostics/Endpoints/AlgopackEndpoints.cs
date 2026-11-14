using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;
using Microsoft.AspNetCore.Mvc;
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
                [FromQuery(Name = "iss.reverse")] int? reverse,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateStart(start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "candles", "stock", validationMessage, ticker);
                }

                string method = $"/engines/stock/markets/shares/boards/tqbr/securities/{ticker}/candles.json";
                Dictionary<string, string> queryParams = CreateRawCandlesQuery(from!, till!, interval ?? 1, reverse, start);
                return await ExecuteRawAsync("candles", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/futures/candles/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? interval,
                [FromQuery(Name = "iss.reverse")] int? reverse,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateStart(start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "candles", "futures", validationMessage, ticker);
                }

                string method = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}/candles.json";
                Dictionary<string, string> queryParams = CreateRawCandlesQuery(from!, till!, interval ?? 1, reverse, start);
                return await ExecuteRawAsync("candles", "futures", ticker, client, method, queryParams, ct);
            });

            // Колонки сырых точек курсорных видов берутся из декларации реестра.
            diagnosticsGroup.MapGet("/raw/market/stock/tradestats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "tradestats", "stock", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/eq/tradestats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.TradeStatsStock.ColumnsParam, latest, start);
                return await ExecuteRawAsync("tradestats", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/futures/tradestats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "tradestats", "futures", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/fo/tradestats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.TradeStatsFutures.ColumnsParam, latest, start);
                return await ExecuteRawAsync("tradestats", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/stock/obstats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "obstats", "stock", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/eq/obstats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.ObStatsStock.ColumnsParam, latest, start);
                return await ExecuteRawAsync("obstats", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/futures/obstats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "obstats", "futures", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/fo/obstats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.ObStatsFutures.ColumnsParam, latest, start);
                return await ExecuteRawAsync("obstats", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/stock/orderstats/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "orderstats", "stock", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/eq/orderstats/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.OrderStatsStock.ColumnsParam, latest, start);
                return await ExecuteRawAsync("orderstats", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/futures/futoi/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "futoi", "futures", validationMessage, ticker);
                }

                string method = $"/analyticalproducts/futoi/securities/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawFutoiQuery(from!, till!, latest, start);
                return await ExecuteRawAsync("futoi", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/stock/hi2/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "hi2", "stock", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/eq/hi2/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.Hi2Stock.ColumnsParam, latest, start);
                return await ExecuteRawAsync("hi2", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/futures/hi2/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "hi2", "futures", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/fo/hi2/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.Hi2Futures.ColumnsParam, latest, start);
                return await ExecuteRawAsync("hi2", "futures", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/stock/alerts/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "alerts", "stock", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/eq/alerts/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.MegaAlertsStock.ColumnsParam, latest, start);
                return await ExecuteRawAsync("alerts", "stock", ticker, client, method, queryParams, ct);
            });

            diagnosticsGroup.MapGet("/raw/market/futures/alerts/{ticker}", async (
                string ticker,
                string? from,
                string? till,
                int? latest,
                int? start,
                MoexHttpAlgClient client,
                CancellationToken ct) =>
            {
                string? validationMessage = ValidateRangeRequest(ticker, from, till);
                validationMessage ??= ValidateLatestAndStart(latest, start);
                if (validationMessage is not null)
                {
                    return DiagnosticResponses.CreateDiagnosticError(400, DiagnosticSource, "alerts", "futures", validationMessage, ticker);
                }

                string method = $"/datashop/algopack/fo/alerts/{ticker}.json";
                Dictionary<string, string> queryParams = CreateRawDataQuery(from!, till!, MoexSeriesRegistry.MegaAlertsFutures.ColumnsParam, latest, start);
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
                return DiagnosticResponses.CreateDiagnosticError(500, DiagnosticSource, kind, market, ex.Message, ticker);
            }
        }

        private static Dictionary<string, string> CreateRawCandlesQuery(string from, string till, int interval, int? reverse, int? start)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["interval"] = interval.ToString(),
                ["from"] = from,
                ["till"] = till,
                ["iss.meta"] = "off",
                ["iss.only"] = "candles"
            };

            if (reverse is not null)
                queryParams["iss.reverse"] = reverse.Value.ToString();
            if (start is not null)
                queryParams["start"] = start.Value.ToString();

            return queryParams;
        }

        private static Dictionary<string, string> CreateRawDataQuery(string from, string till, string columns, int? latest, int? start)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["from"] = from,
                ["till"] = till,
                ["iss.meta"] = "off",
                ["iss.only"] = "data,data.cursor",
                ["data.columns"] = columns
            };

            AddLatestAndStart(queryParams, latest, start);
            return queryParams;
        }

        private static Dictionary<string, string> CreateRawFutoiQuery(string from, string till, int? latest, int? start)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["from"] = from,
                ["till"] = till,
                ["iss.meta"] = "off"
            };

            AddLatestAndStart(queryParams, latest, start);
            return queryParams;
        }

        private static void AddLatestAndStart(Dictionary<string, string> queryParams, int? latest, int? start)
        {
            if (latest is not null)
                queryParams["latest"] = latest.Value.ToString();
            if (start is not null)
                queryParams["start"] = start.Value.ToString();
        }

        private static string? ValidateLatestAndStart(int? latest, int? start)
        {
            if (latest is not null && latest is not 0 and not 1)
                return "Query parameter latest must be 0 or 1.";

            return ValidateStart(start);
        }

        private static string? ValidateStart(int? start)
        {
            if (start is < 0)
                return "Query parameter start must be a non-negative integer.";

            return null;
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

    }
}    

