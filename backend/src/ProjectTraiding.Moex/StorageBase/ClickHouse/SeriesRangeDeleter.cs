using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Diagnostics;
using System.Globalization;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

public sealed class SeriesRangeDeleter
{
    private readonly ClickHouseClient _client;
    private readonly ILogger<SeriesRangeDeleter> _logger;

    public SeriesRangeDeleter(
        ClickHouseClient client,
        ILogger<SeriesRangeDeleter> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task DeleteAsync(
        MoexSeriesSpec spec,
        string secid,
        DateOnly from,
        DateOnly tillInclusive,
        CancellationToken ct)
    {
        if (spec.DataKind == "tradestats")
        {
            await DeleteTableRangeAsync(spec.Table, "source_time", secid, from, tillInclusive, ct);
            await DeleteTableRangeAsync("moex_candles_5m", "begin", secid, from, tillInclusive, ct);
            return;
        }

        string timeColumn = spec.DataKind == "candles" ? "begin" : "source_time";
        await DeleteTableRangeAsync(spec.Table, timeColumn, secid, from, tillInclusive, ct);
    }

    private async Task DeleteTableRangeAsync(
        string table,
        string timeColumn,
        string secid,
        DateOnly from,
        DateOnly tillInclusive,
        CancellationToken ct)
    {
        long startTs = Stopwatch.GetTimestamp();
        string fromText = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + " 00:00:00.000";
        string tillText = tillInclusive.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + " 00:00:00.000";
        string rangeCondition = $"secid = {{secid:String}}\n" +
            $"AND {timeColumn} >= toDateTime64({{from:String}}, 3, 'Europe/Moscow')\n" +
            $"AND {timeColumn} <  toDateTime64({{till:String}}, 3, 'Europe/Moscow')";
        ClickHouseParameterCollection parameters = new ClickHouseParameterCollection();
        parameters.Add(new ClickHouseDbParameter
        {
            ParameterName = "secid",
            ClickHouseType = "String",
            Value = secid,
        });
        parameters.Add(new ClickHouseDbParameter
        {
            ParameterName = "from",
            ClickHouseType = "String",
            Value = fromText,
        });
        parameters.Add(new ClickHouseDbParameter
        {
            ParameterName = "till",
            ClickHouseType = "String",
            Value = tillText,
        });

        string existsSql = $"SELECT 1 FROM {table} WHERE {rangeCondition} LIMIT 1";
        QueryOptions existsOptions = new QueryOptions();
        object? scalar = await _client.ExecuteScalarAsync(
            existsSql, parameters, existsOptions, ct);
        if (scalar is null or DBNull)
            return;

        string deleteSql = $"ALTER TABLE {table} DELETE WHERE {rangeCondition}";
        QueryOptions deleteOptions = new QueryOptions
        {
            CustomSettings = new Dictionary<string, object>
            {
                ["mutations_sync"] = 1,
            },
        };
        await _client.ExecuteNonQueryAsync(deleteSql, parameters, deleteOptions, ct);

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
        MoexWriterLogMessages.SeriesRangeDeleted(
            _logger, secid, table, fromText, tillText, elapsed);
    }
}
