using System.Globalization;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Copy;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

/// <summary>
/// Ручная диагностическая проверка прямой POCO-вставки минутной свечи.
/// Метод намеренно не подключён к рабочему конвейеру.
/// </summary>
public static class MoexCandlePocoInsertProbe
{
    public static async Task RunAsync(
        ClickHouseClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        string tableName = $"moex_poco_candles_probe_{Guid.NewGuid():N}";
        string createSql = $"CREATE TABLE {tableName} AS moex_candles_1m ENGINE = Memory";
        bool tableCreated = false;
        Exception? operationException = null;

        try
        {
            await client.ExecuteNonQueryAsync(createSql, null, null, cancellationToken);
            tableCreated = true;

            client.RegisterBinaryInsertType<MoexCandleDTO>();

            DateTime begin = new(2025, 3, 27, 10, 11, 12, 345, DateTimeKind.Unspecified);
            DateTime end = new(2025, 3, 27, 10, 12, 0, 678, DateTimeKind.Unspecified);
            List<MoexCandleDTO> candles =
            [
                new MoexCandleDTO
                {
                    SecId = "POCO_PROBE",
                    Open = 101.125,
                    Close = 101.25,
                    High = 102.5,
                    Low = 100.75,
                    Value = null,
                    Volume = 12345.0,
                    Begin = begin,
                    End = end,
                    IngestPriority = 7,
                },
            ];

            InsertOptions insertOptions = new()
            {
                BatchSize = candles.Count,
                MaxDegreeOfParallelism = 1,
                Format = RowBinaryFormat.RowBinary,
            };

            long rowsInserted = await client.InsertBinaryAsync(
                tableName,
                candles,
                insertOptions,
                cancellationToken);

            if (rowsInserted != candles.Count)
            {
                throw new InvalidOperationException(
                    $"ClickHouse POCO-проверка: ожидалось вставить строк {candles.Count}, " +
                    $"драйвер подтвердил {rowsInserted}.");
            }

            string selectSql = $"""
                SELECT
                    secid,
                    open,
                    close,
                    high,
                    low,
                    value,
                    volume,
                    toString(begin),
                    toString(end),
                    ingest_priority
                FROM {tableName}
                ORDER BY begin
                """;

            ClickHouseDataReader reader = await client.ExecuteReaderAsync(
                selectSql,
                null,
                null,
                cancellationToken);

            using (reader)
            {
                int rowIndex = 0;
                while (reader.Read())
                {
                    if (rowIndex >= candles.Count)
                    {
                        rowIndex++;
                        continue;
                    }

                    MoexCandleDTO expected = candles[rowIndex];
                    string prefix = $"строка {rowIndex}, ";
                    EnsureFieldValue(prefix + "secid", expected.SecId, reader.GetValue(0));
                    EnsureFieldValue(prefix + "open", expected.Open, reader.GetValue(1));
                    EnsureFieldValue(prefix + "close", expected.Close, reader.GetValue(2));
                    EnsureFieldValue(prefix + "high", expected.High, reader.GetValue(3));
                    EnsureFieldValue(prefix + "low", expected.Low, reader.GetValue(4));
                    EnsureFieldValue(prefix + "value", expected.Value, reader.GetValue(5));
                    EnsureFieldValue(prefix + "volume", expected.Volume, reader.GetValue(6));
                    EnsureFieldValue(
                        prefix + "begin",
                        expected.Begin.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        reader.GetValue(7));
                    EnsureFieldValue(
                        prefix + "end",
                        expected.End?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        reader.GetValue(8));
                    EnsureFieldValue(
                        prefix + "ingest_priority",
                        expected.IngestPriority,
                        reader.GetValue(9));

                    rowIndex++;
                }

                if (rowIndex != candles.Count)
                {
                    throw new InvalidOperationException(
                        $"ClickHouse POCO-проверка: ожидалось строк {candles.Count}, " +
                        $"прочитано {rowIndex}.");
                }
            }
        }
        catch (Exception exception)
        {
            operationException = exception;
            throw;
        }
        finally
        {
            if (tableCreated)
            {
                string dropSql = $"DROP TABLE IF EXISTS {tableName}";
                try
                {
                    await client.ExecuteNonQueryAsync(dropSql, null, null, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    string cleanupMessage =
                        $"Не удалось удалить контрольную таблицу ClickHouse '{tableName}'.";

                    if (operationException is null)
                    {
                        throw new InvalidOperationException(cleanupMessage, cleanupException);
                    }

                    operationException.Data["ClickHouseProbeCleanupMessage"] = cleanupMessage;
                    operationException.Data["ClickHouseProbeCleanupException"] = cleanupException;
                }
            }
        }
    }

    private static void EnsureFieldValue(string fieldName, object? expected, object? actual)
    {
        object? normalizedActual = actual is DBNull ? null : actual;
        if (Equals(expected, normalizedActual))
            return;

        string expectedText = FormatValue(expected);
        string actualText = FormatValue(normalizedActual);
        throw new InvalidOperationException(
            $"ClickHouse POCO-проверка: поле '{fieldName}' ожидалось '{expectedText}', " +
            $"получено '{actualText}'.");
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
            return "null";

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return value.ToString() ?? string.Empty;
    }
}
