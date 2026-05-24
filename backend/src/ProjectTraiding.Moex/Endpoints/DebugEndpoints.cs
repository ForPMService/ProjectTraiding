using ProjectTraiding.Moex.Clients;
using System.Text.Json;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Debug endpoint для сбора columns[] со всех MOEX endpoint'ов.
    /// Вызвать один раз: GET http://localhost:5025/debug/columns-map
    /// После получения результата — удалить этот файл.
    /// </summary>
    public static class DebugEndpoints
    {
        private record ColumnResult(string RootKey, List<string> Columns);
        private record ErrorResult(string Message);

        public static IEndpointRouteBuilder MapDebugEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/debug/columns-map", async (
                MoexHttpAlgClient algClient,
                MoexHttpIssClient issClient,
                CancellationToken ct) =>
            {
                var map = new Dictionary<string, object>();

                // ═══════════════════════════════════════════════
                // ALG endpoints (требуют ключ ALGOPACK)
                // ═══════════════════════════════════════════════

                var algEndpoints = new (string name, string url, string rootKey)[]
                {
                    ("Candles (stock SBER)",
                     "/engines/stock/markets/shares/boards/tqbr/securities/SBER/candles.json?interval=1&from=2026-05-05&till=2026-05-05",
                     "candles"),

                    ("Candles (futures SiM6)",
                     "/engines/futures/markets/forts/boards/RFUD/securities/SiM6/candles.json?interval=1&from=2026-05-05&till=2026-05-05",
                     "candles"),

                    ("TradeStats (stock SBER)",
                     "/datashop/algopack/eq/tradestats/SBER.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("TradeStats (futures SiM6)",
                     "/datashop/algopack/fo/tradestats/SiM6.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("OBStats (stock SBER)",
                     "/datashop/algopack/eq/obstats/SBER.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("OBStats (futures SiM6)",
                     "/datashop/algopack/fo/obstats/SiM6.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("OrderStats (stock SBER)",
                     "/datashop/algopack/eq/orderstats/SBER.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("Hi2 (stock SBER)",
                     "/datashop/algopack/eq/hi2/SBER.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("Hi2 (futures SiM6)",
                     "/datashop/algopack/fo/hi2/SiM6.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("MegaAlerts (stock SBER)",
                     "/datashop/algopack/eq/alerts/SBER.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("MegaAlerts (futures SiM6)",
                     "/datashop/algopack/fo/alerts/SiM6.json?from=2026-05-05&till=2026-05-05",
                     "data"),

                    ("Futoi (Si)",
                     "/analyticalproducts/futoi/securities/Si.json?from=2026-05-05&till=2026-05-05",
                     "futoi"),
                };

                foreach (var (name, url, rootKey) in algEndpoints)
                {
                    try
                    {
                        string raw = await algClient.GetRaw(url, cancellationToken: ct);
                        ExtractColumns(raw, rootKey, name, map);
                    }
                    catch (Exception ex)
                    {
                        map[name] = new ErrorResult(ex.Message);
                    }
                }

                // ═══════════════════════════════════════════════
                // ISS endpoints (без ключа)
                // ═══════════════════════════════════════════════

                var issEndpoints = new (string name, string url, string rootKey)[]
                {
                    ("Securities (stock TQBR)",
                     "/engines/stock/markets/shares/boards/tqbr/securities.json",
                     "securities"),

                    ("Securities (futures RFUD)",
                     "/engines/futures/markets/forts/boards/RFUD/securities.json",
                     "securities"),
                };

                foreach (var (name, url, rootKey) in issEndpoints)
                {
                    try
                    {
                        string raw = await issClient.GetRaw(url, ct);
                        ExtractColumns(raw, rootKey, name, map);
                    }
                    catch (Exception ex)
                    {
                        map[name] = new ErrorResult(ex.Message);
                    }
                }

                // ═══════════════════════════════════════════════
                // Calendar endpoints (требуют ключ ALGOPACK)
                // ═══════════════════════════════════════════════

                var calendarEndpoints = new (string name, string url, string[] rootKeys)[]
                {
                    ("Calendar OffDays All",
                     "/calendars.json",
                     new[] { "off_days" }),

                    ("Calendar Stock OffDays",
                     "/calendars/stock.json",
                     new[] { "off_days" }),

                    ("Calendar Futures OffDays",
                     "/calendars/futures.json",
                     new[] { "off_days" }),

                    ("Calendar Stock Session",
                     "/calendars/stock/session.json",
                     new[] { "session_schedule", "session_schedule.types" }),

                    ("Calendar Futures Session",
                     "/calendars/futures/session.json",
                     new[] { "session_schedule", "session_schedule.types" }),

                    ("Calendar Futures Securities",
                     "/calendars/futures/securities.json",
                     new[] { "forts", "options" }),

                    ("Calendar Suspended",
                     "/calendars/stock/securities/suspended/details.json",
                     new[] { "suspended", "suspended.reasons", "suspended.cursor" }),

                    ("Calendar Security Changes",
                     "/calendars/stock/securities/changes.json",
                     new[] { "securities", "securities.attributes", "securities.cursor" }),
                };

                foreach (var (name, url, rootKeys) in calendarEndpoints)
                {
                    try
                    {
                        string raw = await algClient.GetRaw(url, cancellationToken: ct);

                        foreach (string rootKey in rootKeys)
                        {
                            string entryName = $"{name} -> {rootKey}";
                            ExtractColumns(raw, rootKey, entryName, map);
                        }
                    }
                    catch (Exception ex)
                    {
                        map[name] = new ErrorResult(ex.Message);
                    }
                }

                // ═══════════════════════════════════════════════
                // Сериализация через Utf8JsonWriter (без reflection)
                // ═══════════════════════════════════════════════

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();

                    foreach (var (key, value) in map)
                    {
                        writer.WritePropertyName(key);

                        if (value is ColumnResult cr)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("rootKey", cr.RootKey);
                            writer.WriteNumber("columnCount", cr.Columns.Count);
                            writer.WriteStartArray("columns");
                            foreach (var col in cr.Columns)
                                writer.WriteStringValue(col);
                            writer.WriteEndArray();
                            writer.WriteEndObject();
                        }
                        else if (value is ErrorResult er)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("error", er.Message);
                            writer.WriteEndObject();
                        }
                    }

                    writer.WriteEndObject();
                }

                return Results.Bytes(stream.ToArray(), "application/json");
            });
            routes.MapGet("/debug/futoi-raw", async (
                MoexHttpAlgClient algClient,
                CancellationToken ct) =>
            {
                string raw = await algClient.GetRaw(
                    "/analyticalproducts/futoi/securities/Si.json?from=2026-05-05&till=2026-05-08",
                    cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });
            // ── В DebugEndpoints.cs, внутри MapDebugEndpoints, перед return routes; ──

            // ═══════════════════════════════════════════════
            // Фаза 2–4: Real-time raw — orderbook, trades, candles today
            // Все через APIM (требуют Bearer)
            // ═══════════════════════════════════════════════

            // ── Orderbook ──

            routes.MapGet("/debug/orderbook-stock-raw", async (
                MoexHttpAlgClient algClient,
                CancellationToken ct) =>
            {
                string raw = await algClient.GetRaw(
                    "/engines/stock/markets/shares/boards/TQBR/securities/SBER/orderbook.json",
                    cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            routes.MapGet("/debug/orderbook-futures-raw", async (
                MoexHttpAlgClient algClient,
                CancellationToken ct) =>
            {
                string raw = await algClient.GetRaw(
                    "/engines/futures/markets/forts/boards/RFUD/securities/SVM6/orderbook.json",
                    cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            // ── Trades ──

            routes.MapGet("/debug/trades-stock-raw", async (
                MoexHttpAlgClient algClient,
                CancellationToken ct) =>
            {
                string raw = await algClient.GetRaw(
                    "/engines/stock/markets/shares/boards/TQBR/securities/SBER/trades.json",
                    cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            routes.MapGet("/debug/trades-futures-raw", async (
                MoexHttpAlgClient algClient,
                CancellationToken ct) =>
            {
                string raw = await algClient.GetRaw(
                    "/engines/futures/markets/forts/boards/RFUD/securities/SVM6/trades.json",
                    cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            // ── Candles today ──

            routes.MapGet("/debug/candles-today-stock-raw", async (
                MoexHttpAlgClient algClient,
                CancellationToken ct) =>
            {
                string today = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");
                string raw = await algClient.GetRaw(
                    $"/engines/stock/markets/shares/boards/TQBR/securities/SBER/candles.json?interval=1&from={today}&till={today}",
                    cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            routes.MapGet("/debug/candles-today-futures-raw", async (
                MoexHttpAlgClient algClient,
                CancellationToken ct) =>
            {
                string today = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");
                string raw = await algClient.GetRaw(
                    $"/engines/futures/markets/forts/boards/RFUD/securities/SVM6/candles.json?interval=1&from={today}&till={today}",
                    cancellationToken: ct);
                return Results.Text(raw, "application/json");
            });

            return routes;
        }

        private static void ExtractColumns(
            string rawJson,
            string rootKey,
            string entryName,
            Dictionary<string, object> map)
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty(rootKey, out var table) &&
                table.TryGetProperty("columns", out var cols))
            {
                var columns = new List<string>();
                for (int i = 0; i < cols.GetArrayLength(); i++)
                    columns.Add(cols[i].GetString() ?? "???");

                map[entryName] = new ColumnResult(rootKey, columns);
            }
            else
            {
                map[entryName] = new ErrorResult($"rootKey '{rootKey}' or 'columns' not found");
            }
        }
    }
}
