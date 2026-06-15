using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.RawCapture;
using ProjectTraiding.Moex.Options;
using System.Text.Json;

namespace ProjectTraiding.Moex.Endpoints
{
    /// <summary>
    /// Временные debug endpoint-ы и отдельные diagnostic raw route-ы для ручной проверки MOEX.
    /// </summary>
    public static class DebugEndpoints
    {
        private record ColumnResult(string RootKey, List<string> Columns);
        private record ErrorResult(string Message);

        public static IEndpointRouteBuilder MapTemporaryDebugEndpoints(this IEndpointRouteBuilder routes)
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
            routes.MapGet("/debug/s3/health", async (
    IAmazonS3 s3Client,
    IOptions<RawCaptureOptions> captureOptions,
    CancellationToken ct) =>
            {
                RawCaptureOptions options = captureOptions.Value;

                using MemoryStream stream = new MemoryStream();
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();

                    if (options.Mode == CaptureMode.Off)
                    {
                        writer.WriteString("status", "disabled");
                        writer.WriteString("bucket", options.Bucket);
                        writer.WriteString("mode", "Off");
                    }
                    else
                    {
                        try
                        {
                            await s3Client.GetBucketLocationAsync(options.Bucket, ct);
                            writer.WriteString("status", "ok");
                            writer.WriteString("bucket", options.Bucket);
                            writer.WriteString("mode", options.Mode.ToString());
                        }
                        catch (Exception ex)
                        {
                            writer.WriteString("status", "error");
                            writer.WriteString("bucket", options.Bucket);
                            writer.WriteString("mode", options.Mode.ToString());
                            writer.WriteString("errorType", ex.GetType().Name);
                            writer.WriteString("message", ex.Message);
                        }
                    }

                    writer.WriteEndObject();
                }

                return Results.Bytes(stream.ToArray(), "application/json");
            });
            routes.MapGet("/debug/s3/write-test", async (
                MoexRawCaptureWriter captureWriter,
                IAmazonS3 s3Client,
                IOptions<RawCaptureOptions> captureOptions,
                CancellationToken ct) =>
            {
                RawCaptureOptions options = captureOptions.Value;

                using MemoryStream jsonStream = new MemoryStream();
                using (Utf8JsonWriter writer = new Utf8JsonWriter(jsonStream))
                {
                    writer.WriteStartObject();

                    if (options.Mode == CaptureMode.Off)
                    {
                        writer.WriteString("status", "disabled");
                        writer.WriteString("detail", "RawCapture:Mode is Off — writer skips all writes. Set Mode to FailedOnly or higher to test.");
                        writer.WriteEndObject();
                        return Results.Bytes(jsonStream.ToArray(), "application/json");
                    }

                    string testKey = "test/write-test-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + ".json";
                    byte[] testPayload = System.Text.Encoding.UTF8.GetBytes(
                        "{\"test\":true,\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}");

                    // Шаг 1: запись через MoexRawCaptureWriter (DI → Writer → S3)
                    await captureWriter.TryCaptureAsync(testKey, testPayload, ct);

                    // Шаг 2: чтение обратно через IAmazonS3
                    try
                    {
                        GetObjectResponse readBack = await s3Client.GetObjectAsync(
                            options.Bucket, testKey, ct);

                        using MemoryStream readStream = new MemoryStream();
                        await readBack.ResponseStream.CopyToAsync(readStream, ct);
                        int bytesRead = (int)readStream.Length;

                        // Шаг 3: удаление тестового объекта
                        try
                        {
                            await s3Client.DeleteObjectAsync(options.Bucket, testKey, ct);
                        }
                        catch
                        {
                            // Не критично — тестовый объект останется в бакете
                        }

                        if (bytesRead == testPayload.Length)
                        {
                            writer.WriteString("status", "ok");
                            writer.WriteString("key", testKey);
                            writer.WriteNumber("bytesWritten", testPayload.Length);
                            writer.WriteNumber("bytesReadBack", bytesRead);
                            writer.WriteString("mode", options.Mode.ToString());
                            writer.WriteString("bucket", options.Bucket);
                        }
                        else
                        {
                            writer.WriteString("status", "size_mismatch");
                            writer.WriteString("key", testKey);
                            writer.WriteNumber("bytesWritten", testPayload.Length);
                            writer.WriteNumber("bytesReadBack", bytesRead);
                        }
                    }
                    catch (Exception ex)
                    {
                        writer.WriteString("status", "read_back_failed");
                        writer.WriteString("key", testKey);
                        writer.WriteNumber("bytesWritten", testPayload.Length);
                        writer.WriteString("errorType", ex.GetType().Name);
                        writer.WriteString("message", ex.Message);
                        writer.WriteString("detail",
                            "TryCaptureAsync completed without exception but object not found on read-back. " +
                            "Check RawCaptureLogMessages for capture errors.");
                    }

                    writer.WriteEndObject();
                }

                return Results.Bytes(jsonStream.ToArray(), "application/json");
            });

            return routes;
        }

        public static IEndpointRouteBuilder MapDiagnosticDebugEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/raw/market-statistics-marketdata-stock/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["iss.only"] = "marketdata",
                    ["iss.meta"] = "off",
                };
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
                return Results.Text(raw, "application/json");
            });

            routes.MapGet("/raw/market-statistics-marketdata-futures/{ticker}", async (
                string ticker,
                MoexRealtimeRestClient client,
                CancellationToken ct) =>
            {
                string url = $"/engines/futures/markets/forts/boards/RFUD/securities/{ticker}.json";
                Dictionary<string, string> queryParams = new Dictionary<string, string>
                {
                    ["iss.only"] = "marketdata",
                    ["iss.meta"] = "off",
                };
                string raw = await client.GetRawSectionAsync(url, queryParams, ct);
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
