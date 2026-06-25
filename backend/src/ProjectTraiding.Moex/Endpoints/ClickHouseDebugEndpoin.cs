using ClickHouse.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Endpoints
{

    /// <summary>
    /// Диагностическая точка проверки соединения с ClickHouse.
    /// Не будущий API витрины — ручная проверка, что приложение видит базу.
    /// </summary>
    public static class ClickHouseDebugEndpoints
    {
        public static IEndpointRouteBuilder MapClickHouseDebugEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/debug/clickhouse");

            group.MapGet("/ping", async (ClickHouseClient client) =>
            {
                var version = await client.ExecuteScalarAsync("SELECT version()");
                return Results.Text(version?.ToString() ?? "null");
            });

            group.MapGet("/insert-probe", async (ClickHouseClient client) =>
            {
                // Чужой год 2099 — потом снесём партицию целиком.
                // Kind=Unspecified — московское стенное время, как в столбце DateTime64(3,'Europe/Moscow').
                var begin = new DateTime(2099, 1, 1, 12, 34, 56, 789, DateTimeKind.Unspecified);

                // Порядок значений строго по столбцам таблицы.
                var row = new object[]
                {
        "PING2099",   // secid   LowCardinality(String)
        begin,        // begin   DateTime64(3,'Europe/Moscow')
        begin,        // end     Nullable(DateTime64)  — даём значение, не null
        1.0,          // open    Nullable(Float64)
        2.0,          // high
        0.5,          // low
        1.5,          // close
        100.0,        // value
        10.0          // volume
                };

                string[] columns = { "secid", "begin", "end", "open", "high", "low", "close", "value", "volume" };

                long inserted = await client.InsertBinaryAsync("moex_candles_1m", columns, new[] { row });

                object? readBack = await client.ExecuteScalarAsync(
                    "SELECT begin FROM moex_candles_1m WHERE secid = 'PING2099' ORDER BY begin LIMIT 1");

                return Results.Text($"inserted={inserted}; readBack begin={readBack:O}");
            });

            return routes;
        }
    }
}
