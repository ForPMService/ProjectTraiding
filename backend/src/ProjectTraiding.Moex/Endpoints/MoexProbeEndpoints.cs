using ProjectTraiding.Moex.Clients;
using System.Text.Json;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Исследовательский «пробник» формы ответа MOEX.
    /// Цель: дёрнуть сырой JSON по ЛЮБОМУ методу MOEX с минимальным окном данных
    /// и вернуть его, обрезав строки до нескольких штук — чтобы посмотреть ФОРМУ:
    /// какие корневые блоки, массив columns, блок metadata (типы), блок *.cursor.
    ///
    /// Регистрируется только в development-контуре временных debug-маршрутов.
    /// Не часть публичного API.
    ///
    /// Пример:
    ///   GET /debug/probe?path=/datashop/algopack/eq/tradestats/SBER.json&amp;date=2026-05-05
    ///   GET /debug/probe?path=/engines/stock/markets/shares/boards/tqbr/securities.json&amp;client=iss
    ///   GET /debug/probe?path=/datashop/algopack/fo/obstats/SiM6.json&amp;rows=0   (только структура, 0 строк)
    /// </summary>
    public static class MoexProbeEndpoints
    {
        public static IEndpointRouteBuilder MapMoexProbeEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/debug/probe", async (
                HttpContext ctx,
                MoexHttpAlgClient algClient,
                MoexHttpIssClient issClient,
                string path,
                string? client,
                int? rows,
                string? date,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Results.BadRequest("Укажи ?path=<метод MOEX>, например /datashop/algopack/eq/tradestats/SBER.json");
                }

                // Окно по умолчанию — вчера по МСК. Можно переопределить ?date=YYYY-MM-DD
                // (для рядов лучше задать заведомо торговый день, чтобы увидеть строки-примеры).
                string day = string.IsNullOrWhiteSpace(date)
                    ? DateTime.UtcNow.AddHours(3).AddDays(-1).ToString("yyyy-MM-dd")
                    : date;

                int rowCap = rows ?? 3;            // сколько строк данных оставить (0 = только форма)
                if (rowCap < 0) rowCap = 0;

                string finalPath = BuildProbePath(path, day);

                // Выбор клиента: alg (APIM, Bearer) по умолчанию; iss (публичный) для справочников.
                bool useIss = string.Equals(client, "iss", StringComparison.OrdinalIgnoreCase);

                string raw;
                try
                {
                    raw = useIss
                        ? await issClient.GetRaw(finalPath, ct)
                        : await algClient.GetRaw(finalPath, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    // Сетевые/HTTP-ошибки GetRaw не типизирует — отдаём как есть, чтобы было видно.
                    ctx.Response.Headers["X-Moex-Probe-Path"] = finalPath;
                    return Results.Text($"{{\"probe_error\":\"{Escape(ex.Message)}\"}}", "application/json");
                }

                ctx.Response.Headers["X-Moex-Probe-Path"] = finalPath;   // путь без ключа — что именно дёрнули

                // Обрезаем массивы data до rowCap, сохраняя columns/metadata/cursor нетронутыми.
                byte[] trimmed;
                try
                {
                    trimmed = TrimRows(raw, rowCap);
                }
                catch (JsonException)
                {
                    // Не разобрали как JSON — вернём сырой ответ как есть.
                    return Results.Text(raw, "application/json");
                }

                return Results.Bytes(trimmed, "application/json");
            });

            return routes;
        }

        /// <summary>
        /// Достраивает путь минимальными параметрами формы:
        /// для датированных рядов — окно from=till=day; для свечей — interval=1; везде — iss.meta=on.
        /// Уже заданные параметры не перетираются.
        /// </summary>
        private static string BuildProbePath(string path, string day)
        {
            string[] parts = path.Split('?', 2);
            string p = parts[0].StartsWith('/') ? parts[0] : "/" + parts[0];

            var q = new List<string>();
            if (parts.Length > 1 && parts[1].Length > 0)
                q.AddRange(parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries));

            bool Has(string key) =>
                q.Exists(s => s.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));

            bool needsWindow =
                p.Contains("/datashop/algopack/", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("/candles", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("/futoi/", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("/calendars", StringComparison.OrdinalIgnoreCase);

            if (needsWindow && !Has("from"))
            {
                q.Add("from=" + day);
                q.Add("till=" + day);
            }
            if (p.Contains("/candles", StringComparison.OrdinalIgnoreCase) && !Has("interval"))
                q.Add("interval=1");

            // metadata = типы колонок: это часть формы, оставляем включённым по умолчанию.
            if (!Has("iss.meta"))
                q.Add("iss.meta=on");

            return q.Count > 0 ? p + "?" + string.Join("&", q) : p;
        }

        /// <summary>
        /// Копирует JSON, усекая каждый массив с именем "data" до rowCap элементов.
        /// columns, metadata и блоки *.cursor сохраняются целиком (cursor — 1 строка, в cap влезает).
        /// Без reflection — JsonDocument + Utf8JsonWriter (AOT-совместимо).
        /// </summary>
        private static byte[] TrimRows(string rawJson, int rowCap)
        {
            using var doc = JsonDocument.Parse(rawJson);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteTrimmed(writer, doc.RootElement, rowCap);
            }
            return stream.ToArray();
        }

        private static void WriteTrimmed(Utf8JsonWriter w, JsonElement el, int rowCap)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    w.WriteStartObject();
                    foreach (JsonProperty prop in el.EnumerateObject())
                    {
                        w.WritePropertyName(prop.Name);
                        // "data" внутри блока ISS — это массив строк; именно его усекаем.
                        if (prop.Name == "data" && prop.Value.ValueKind == JsonValueKind.Array)
                            WriteCappedArray(w, prop.Value, rowCap);
                        else
                            WriteTrimmed(w, prop.Value, rowCap);
                    }
                    w.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    w.WriteStartArray();
                    foreach (JsonElement item in el.EnumerateArray())
                        WriteTrimmed(w, item, rowCap);
                    w.WriteEndArray();
                    break;

                default:
                    el.WriteTo(w);   // строки/числа/bool/null — как есть
                    break;
            }
        }

        private static void WriteCappedArray(Utf8JsonWriter w, JsonElement arr, int rowCap)
        {
            w.WriteStartArray();
            int i = 0;
            foreach (JsonElement item in arr.EnumerateArray())
            {
                if (i >= rowCap) break;
                item.WriteTo(w);
                i++;
            }
            w.WriteEndArray();
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
