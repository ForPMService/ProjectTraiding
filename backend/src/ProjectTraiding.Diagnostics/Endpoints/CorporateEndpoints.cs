using ProjectTraiding.Moex.Clients;


namespace ProjectTraiding.Diagnostics.Endpoints
{
    /// <summary>
    /// Diagnostic raw endpoint-ы корпоративных событий MOEX для ручной разведки
    /// контракта источника: дробления, календарь событий эмитента, корпоративные действия.
    ///
    /// Все точки идут через публичный ISS без ключа. Путь зашит в маршруте, параметры
    /// строки запроса передаются источнику без разбора. Обход страниц не выполняется.
    ///
    /// Общие операции построения ответа, проверки пути и передачи параметров вынесены
    /// в DiagnosticResponses, чтобы их контракт не расходился с календарной диагностикой.
    /// </summary>
    public static class CorporateEndpoints
    {
        private const string DiagnosticSource = "MOEX_CORPORATE";

        public static IEndpointRouteBuilder MapCorporateDiagnosticsEndpoints(this IEndpointRouteBuilder routes)
        {
            RouteGroupBuilder diagnosticsGroup = routes.MapGroup("/moex");

            diagnosticsGroup.MapGet("/raw/corporate/splits", async (
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteIssRawAsync(
                    "splits", "stock", request, issClient,
                    "/statistics/engines/stock/splits.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/corporate/ir-calendar", async (
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteIssRawAsync(
                    "ir-calendar", "stock", request, issClient,
                    "/cci/calendars/ir-calendar.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/corporate/corp-actions", async (
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteIssRawAsync(
                    "corp-actions", "stock", request, issClient,
                    "/cci/corp-actions.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/corporate/dividends", async (
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteIssRawAsync(
                    "dividends", "stock", request, issClient,
                    "/cci/corp-actions/dividends.json", ct);
            });

            // Проба устаревшего адреса: в актуальном справочнике методов он отсутствует,
            // но широко используется. Отрицательный ответ здесь — такой же результат
            // разведки, как и успешный.
            diagnosticsGroup.MapGet("/raw/corporate/security-dividends/{ticker}", async (
                string ticker,
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                if (!DiagnosticResponses.IsSafePathSegment(ticker))
                {
                    return DiagnosticResponses.CreateDiagnosticError(
                        400, DiagnosticSource, "security-dividends", "stock", "Route parameter ticker must be alphanumeric.");
                }

                string method = $"/securities/{ticker}/dividends.json";
                return await ExecuteIssRawAsync("security-dividends", "stock", request, issClient, method, ct);
            });

            return routes;
        }

        private static async Task<IResult> ExecuteIssRawAsync(
            string kind,
            string market,
            HttpRequest request,
            MoexHttpIssClient issClient,
            string method,
            CancellationToken ct)
        {
            Dictionary<string, string> queryParams = DiagnosticResponses.CollectPassThroughQuery(request);

            try
            {
                string raw = await issClient.GetRaw(method, queryParams, ct);
                return Results.Text(raw, "application/json");
            }
            catch (Exception ex)
            {
                return DiagnosticResponses.CreateDiagnosticError(500, DiagnosticSource, kind, market, ex.Message);
            }
        }

    }
}
