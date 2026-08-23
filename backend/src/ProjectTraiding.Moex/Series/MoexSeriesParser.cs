using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

public sealed class MoexSeriesParser
{
    public List<(object?[] Row, DateTime Time)> Parse(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        out PaginationCursorDTO cursor)
    {
        List<(object?[] Row, DateTime Time)> rows = [];
        Utf8JsonReader reader = new(body);

        ParseHelpersUtf8.SkipToRootObject(ref reader, spec.RootKey);

        bool foundColumns = false;
        bool foundData = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("columns"u8))
            {
                foundColumns = true;
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(
                    ref reader, spec.SourceColumns, spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }

                foundData = true;
                ReadRows(ref reader, rows, spec, taskSecId);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);

        cursor = new PaginationCursorDTO();
        if (spec.Pagination == PaginationKind.Cursor)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("data.cursor"u8))
                {
                    try
                    {
                        cursor = ParseHelpersUtf8.ReadCursorRootObject(ref reader);
                    }
                    catch (InvalidOperationException ex)
                    {
                        ParseHelpersUtf8.SchemaMismatch(ex.Message);
                    }

                    break;
                }

                reader.Skip();
            }
        }

        return rows;
    }

    private static void ReadRows(
        ref Utf8JsonReader reader,
        List<(object?[] Row, DateTime Time)> rows,
        MoexSeriesSpec spec,
        string taskSecId)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", spec.RootKey);

        // Один рабочий массив переиспользуется всеми строками страницы. Каждая позиция
        // ниже присваивается заново, включая пустое значение, чтобы значения строк
        // не смешивались.
        object?[] sourceValues = new object?[spec.SourceColumns.Length];

        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, " +
                    $"получено {reader.TokenType}.");
            }

            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                if (!reader.Read())
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Неожиданный конец JSON в строке {rowIndex}, " +
                        $"позиция {position}.");
                }

                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Короткая строка данных: ожидалось " +
                        $"{spec.SourceColumns.Length} колонок, получено {position} " +
                        $"(строка {rowIndex}).");
                }

                sourceValues[position] = reader.TokenType == JsonTokenType.Null
                    ? null
                    : ReadValue(
                        ref reader, spec.SourceColumns[position], spec.SourceColumnUsed[position],
                        rowIndex, spec.RootKey);
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался EndArray после " +
                    $"{spec.SourceColumns.Length} колонок (строка {rowIndex}).");
            }

            rows.Add(BuildTargetRow(sourceValues, spec, taskSecId));
            rowIndex++;
        }
    }

    private static object? ReadValue(
        ref Utf8JsonReader reader,
        SourceColumn column,
        bool used,
        int rowIndex,
        string rootKey)
    {
        try
        {
            if (used)
                return ReadUsedValue(ref reader, column, rowIndex, rootKey);

            // На эту колонку не ссылается ни одна целевая колонка. Сверка вида токена
            // остаётся, строка не создаётся. У истории такая колонка одна — код инструмента,
            // и он берётся из задачи, а не из ответа.
            if (column.Kind == ColumnKind.String)
            {
                ParseHelpersUtf8.ExpectString(ref reader, rowIndex, column.Position, rootKey);
                return null;
            }

            _ = ReadUsedValue(ref reader, column, rowIndex, rootKey);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            ParseHelpersUtf8.SchemaMismatch(ex.Message);
            throw;
        }
    }

    private static object? ReadUsedValue(
        ref Utf8JsonReader reader,
        SourceColumn column,
        int rowIndex,
        string rootKey)
    {
        return column.Kind switch
        {
            ColumnKind.String => ParseHelpersUtf8.ReadString(
                ref reader, rowIndex, column.Position, rootKey),
            ColumnKind.Int32 => ParseHelpersUtf8.ReadInt(
                ref reader, rowIndex, column.Position, rootKey),
            ColumnKind.Int64 => ParseHelpersUtf8.ReadLong(
                ref reader, rowIndex, column.Position, rootKey),
            ColumnKind.Double => ParseHelpersUtf8.ReadDouble(
                ref reader, rowIndex, column.Position, rootKey),
            ColumnKind.DateTime => ParseHelpersUtf8.ReadDateTimeUtf8(
                ref reader, rowIndex, column.Position, rootKey),
            _ => throw new ArgumentOutOfRangeException(
                nameof(column), column.Kind, "Неизвестный тип колонки источника."),
        };
    }

    private static (object?[] Row, DateTime Time) BuildTargetRow(
        object?[] sourceValues,
        MoexSeriesSpec spec,
        string taskSecId)
    {
        object?[] row = new object?[spec.TargetColumns.Length];
        DateTime sourceTime = default;

        for (int i = 0; i < spec.TargetColumns.Length; i++)
        {
            TargetColumn column = spec.TargetColumns[i];
            object? value = column.FillRule switch
            {
                FillRule.TaskSecId => taskSecId,
                FillRule.Direct => sourceValues[column.SourceIndex],
                FillRule.SourceDateTime => MoexClickHouseTime.BuildSourceTime(
                    sourceValues[column.SourceIndex] as string,
                    sourceValues[column.SecondSourceIndex] as string),
                FillRule.WallClock => MoexClickHouseTime.AsWallClock(
                    (DateTime?)sourceValues[column.SourceIndex]),
                FillRule.ExternalSecId => sourceValues[column.SourceIndex],
                FillRule.Constant => column.Constant,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(column), column.FillRule, "Неизвестное правило заполнения."),
            };

            if (column.FillRule is not FillRule.Direct and not FillRule.ExternalSecId)
                ValidateRequired(column, value);

            row[i] = value;
            if (column.FillRule == FillRule.SourceDateTime)
                sourceTime = (DateTime)value!;
            else if (column.FillRule == FillRule.WallClock
                     && column.Required
                     && value is DateTime wallClockTime)
                sourceTime = wallClockTime;
        }

        // Карты проверяли собственные обязательные поля после source_time:
        // сначала обычные значения строки, затем внешний secid.
        ValidateRequired(row, spec, spec.RequiredDirectIndexes);
        ValidateRequired(row, spec, spec.RequiredExternalSecIdIndexes);

        return (row, sourceTime);
    }

    private static void ValidateRequired(
        object?[] row,
        MoexSeriesSpec spec,
        int[] requiredIndexes)
    {
        for (int i = 0; i < requiredIndexes.Length; i++)
        {
            int columnIndex = requiredIndexes[i];
            ValidateRequired(spec.TargetColumns[columnIndex], row[columnIndex]);
        }
    }

    private static void ValidateRequired(TargetColumn column, object? value)
    {
        if (column.Required
            && (value is null || value is string text && string.IsNullOrWhiteSpace(text)))
        {
            throw new InvalidOperationException(
                column.RequiredMessage
                ?? $"Строка отвергнута: обязательная колонка {column.Name} пуста.");
        }
    }
}
