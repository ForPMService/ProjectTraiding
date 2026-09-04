using ProjectTraiding.CustomFeatures.Clients;
using ProjectTraiding.Moex.Clients;


namespace ProjectTraiding.Diagnostics.Endpoints
{
    /// <summary>
    /// Diagnostic raw endpoint-ы календаря и исторических справочников MOEX
    /// для ручной разведки контракта источника.
    ///
    /// Путь к источнику зашит в каждом маршруте и не управляется извне. Параметры строки
    /// запроса передаются источнику без разбора и без проверки: диагностика не интерпретирует
    /// параметры Московской биржи, ответ источника сам является результатом разведки.
    /// Тело любого ответа источника — успешного, отказа по правам или отказа по разбору
    /// запроса — возвращается вызывающему без разбора. Код состояния ответа источника
    /// при этом не пробрасывается: наружу уходит собственный ответ диагностики.
    ///
    /// Обход страниц не выполняется: маршрут делает ровно один запрос и возвращает ровно
    /// один ответ. Продолжение выборки через start — задача оператора разведки.
    /// </summary>
    public static class CalendarEndpoints
    {
        private const string DiagnosticSource = "MOEX_CALENDAR";

        public static IEndpointRouteBuilder MapCalendarDiagnosticsEndpoints(this IEndpointRouteBuilder routes)
        {
            RouteGroupBuilder diagnosticsGroup = routes.MapGroup("/moex");

            diagnosticsGroup.MapGet("/raw/calendar/stock/days", async (
                string? source,
                HttpRequest request,
                CalendarApimClient calendarClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteSelectableRawAsync(
                    "days", "stock", source, request, calendarClient, issClient,
                    "/calendars/stock.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/calendar/futures/days", async (
                string? source,
                HttpRequest request,
                CalendarApimClient calendarClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteSelectableRawAsync(
                    "days", "futures", source, request, calendarClient, issClient,
                    "/calendars/futures.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/calendar/stock/session", async (
                string? source,
                HttpRequest request,
                CalendarApimClient calendarClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteSelectableRawAsync(
                    "session", "stock", source, request, calendarClient, issClient,
                    "/calendars/stock/session.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/calendar/futures/session", async (
                string? source,
                HttpRequest request,
                CalendarApimClient calendarClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteSelectableRawAsync(
                    "session", "futures", source, request, calendarClient, issClient,
                    "/calendars/futures/session.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/calendar/stock/securities-boards", async (
                string? source,
                HttpRequest request,
                CalendarApimClient calendarClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteSelectableRawAsync(
                    "securities-boards", "stock", source, request, calendarClient, issClient,
                    "/calendars/stock/securities/boards.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/calendar/futures/securities", async (
                string? source,
                HttpRequest request,
                CalendarApimClient calendarClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteSelectableRawAsync(
                    "securities", "futures", source, request, calendarClient, issClient,
                    "/calendars/futures/securities.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/history/stock/listing", async (
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteIssRawAsync(
                    "listing", "stock", request, issClient,
                    "/history/engines/stock/markets/shares/listing.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/history/futures/listing", async (
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteIssRawAsync(
                    "listing", "futures", request, issClient,
                    "/history/engines/futures/markets/forts/listing.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/history/stock/listing/{board}", async (
                string board,
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                if (!DiagnosticResponses.IsSafePathSegment(board))
                {
                    return DiagnosticResponses.CreateDiagnosticError(
                        400, DiagnosticSource, "listing", "stock", "Route parameter board must be alphanumeric.");
                }

                string method = $"/history/engines/stock/markets/shares/boards/{board}/listing.json";
                return await ExecuteIssRawAsync("listing", "stock", request, issClient, method, ct);
            });

            diagnosticsGroup.MapGet("/raw/history/futures/listing/{board}", async (
                string board,
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                if (!DiagnosticResponses.IsSafePathSegment(board))
                {
                    return DiagnosticResponses.CreateDiagnosticError(
                        400, DiagnosticSource, "listing", "futures", "Route parameter board must be alphanumeric.");
                }

                string method = $"/history/engines/futures/markets/forts/boards/{board}/listing.json";
                return await ExecuteIssRawAsync("listing", "futures", request, issClient, method, ct);
            });

            diagnosticsGroup.MapGet("/raw/history/stock/sessions", async (
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteIssRawAsync(
                    "sessions", "stock", request, issClient,
                    "/history/engines/stock/markets/shares/sessions.json", ct);
            });

            diagnosticsGroup.MapGet("/raw/engine/{engine}", async (
                string engine,
                HttpRequest request,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                if (!DiagnosticResponses.IsSafePathSegment(engine))
                {
                    return DiagnosticResponses.CreateDiagnosticError(
                        400, DiagnosticSource, "engine", engine, "Route parameter engine must be alphanumeric.");
                }

                string method = $"/engines/{engine}.json";
                return await ExecuteIssRawAsync("engine", engine, request, issClient, method, ct);
            });
            diagnosticsGroup.MapGet("/raw/calendar", async (
                string? source,
                HttpRequest request,
                CalendarApimClient calendarClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                return await ExecuteSelectableRawAsync(
                    "days",
                    "all",
                    source,
                    request,
                    calendarClient,
                    issClient,
                    "/calendars.json",
                    ct);
            });

            return routes;
        }

        /// <summary>
        /// Выполнение с выбором источника. Служебный параметр source принимает только
        /// два значения и определяет, через какой клиент уйдёт запрос: apim — платный
        /// доступ с ключом, iss — публичный доступ без ключа. Значение по умолчанию — apim,
        /// оно совпадает с поведением рабочего календарного клиента.
        /// </summary>
        private static async Task<IResult> ExecuteSelectableRawAsync(
            string kind,
            string market,
            string? source,
            HttpRequest request,
            CalendarApimClient calendarClient,
            MoexHttpIssClient issClient,
            string method,
            CancellationToken ct)
        {
            string resolvedSource = string.IsNullOrWhiteSpace(source) ? "apim" : source;
            bool useIss = string.Equals(resolvedSource, "iss", StringComparison.OrdinalIgnoreCase);
            bool useApim = string.Equals(resolvedSource, "apim", StringComparison.OrdinalIgnoreCase);

            if (!useIss && !useApim)
            {
                return DiagnosticResponses.CreateDiagnosticError(
                    400, DiagnosticSource, kind, market, "Query parameter source accepts only iss or apim.");
            }

            Dictionary<string, string> queryParams = DiagnosticResponses.CollectPassThroughQuery(
                request, excludeSource: true);

            try
            {
                string raw = useIss
                    ? await issClient.GetRaw(method, queryParams, ct)
                    : await calendarClient.GetRaw(method, queryParams, ct);

                return Results.Text(raw, "application/json");
            }
            catch (Exception ex)
            {
                return DiagnosticResponses.CreateDiagnosticError(500, DiagnosticSource, kind, market, ex.Message);
            }
        }

        private static async Task<IResult> ExecuteIssRawAsync(
            string kind,
            string market,
            HttpRequest request,
            MoexHttpIssClient issClient,
            string method,
            CancellationToken ct)
        {
            Dictionary<string, string> queryParams = DiagnosticResponses.CollectPassThroughQuery(
                request, excludeSource: true);

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
