using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

public sealed class MoexSeriesParser
{
    public SeriesParsedPage Parse(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        out PaginationCursorDTO cursor)
    {
        List<(object?[] Row, DateTime Time)> rows = [];
        int sourceRowsCount = 0;
        List<SkippedSourceRow>? skippedSourceRows = null;
        Utf8JsonReader reader = new(body);

        ParseHelpersUtf8.SkipToRootObject(ref reader, spec.RootKey);

        // Единственная точка запуска строгой проверки видов колонок. Отбрасывается
        // значение, а не смысл: проверка бросает исключение при ошибке декларации,
        // и без этого обращения она не выполнится ни разу. Строку не удалять.
        _ = spec.SourceColumnKindsValid;

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
                ReadRows(
                    ref reader, rows, spec, taskSecId, ref sourceRowsCount, ref skippedSourceRows);
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

        return new SeriesParsedPage(rows, sourceRowsCount, skippedSourceRows);
    }

    private static void ReadRows(
        ref Utf8JsonReader reader,
        List<(object?[] Row, DateTime Time)> rows,
        MoexSeriesSpec spec,
        string taskSecId,
        ref int sourceRowIndex,
        ref List<SkippedSourceRow>? skippedSourceRows)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", spec.RootKey);

        // Один рабочий массив переиспользуется всеми строками страницы. Каждая позиция
        // ниже присваивается заново, включая пустое значение, чтобы значения строк
        // не смешивались.
        object?[] sourceValues = new object?[spec.SourceColumns.Length];
        SourceTimeParts sourceTimeParts = default;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался StartArray строки {sourceRowIndex}, " +
                    $"получено {reader.TokenType}.");
            }

            sourceTimeParts.Reset();

            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                if (!reader.Read())
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Неожиданный конец JSON в строке {sourceRowIndex}, " +
                        $"позиция {position}.");
                }

                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Короткая строка данных: ожидалось " +
                        $"{spec.SourceColumns.Length} колонок, получено {position} " +
                        $"(строка {sourceRowIndex}).");
                }

                sourceValues[position] = null;
                if (reader.TokenType != JsonTokenType.Null)
                {
                    sourceValues[position] = ReadValue(
                        ref reader, spec.SourceColumns[position], spec.SourceColumnUsed[position],
                        ref sourceTimeParts, sourceRowIndex, spec.RootKey);
                }
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался EndArray после " +
                    $"{spec.SourceColumns.Length} колонок (строка {sourceRowIndex}).");
            }

            // Отказ значения отвергает одну строку. Несовпадение схемы сюда не попадает:
            // оно объявлено своим типом и поднимается наружу, отменяя задание целиком.
            // Неизвестное правило заполнения тоже сюда не попадает: это ошибка декларации,
            // и тихо пропускать из-за неё данные нельзя.
            try
            {
                rows.Add(BuildTargetRow(sourceValues, in sourceTimeParts, spec, taskSecId));
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or FormatException)
            {
                skippedSourceRows ??= new List<SkippedSourceRow>();
                skippedSourceRows.Add(new SkippedSourceRow(
                    sourceRowIndex, TryReadSourceTime(in sourceTimeParts)));
            }

            sourceRowIndex++;
        }
    }

    private static object? ReadValue(
        ref Utf8JsonReader reader,
        SourceColumn column,
        bool used,
        ref SourceTimeParts sourceTimeParts,
        int rowIndex,
        string rootKey)
    {
        try
        {
            if (used)
                return ReadUsedValue(ref reader, column, ref sourceTimeParts, rowIndex, rootKey);

            // На эту колонку не ссылается ни одна целевая колонка. Сверка вида токена
            // остаётся, строка не создаётся. У истории такая колонка одна — код инструмента,
            // и он берётся из задачи, а не из ответа.
            if (column.Kind == ColumnKind.String)
            {
                ParseHelpersUtf8.ExpectString(ref reader, rowIndex, column.Position, rootKey);
                return null;
            }

            _ = ReadUsedValue(ref reader, column, ref sourceTimeParts, rowIndex, rootKey);
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
        ref SourceTimeParts sourceTimeParts,
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
            ColumnKind.MomentDate => ReadMomentDate(
                ref reader, column, ref sourceTimeParts, rowIndex, rootKey),
            ColumnKind.MomentTime => ReadMomentTime(
                ref reader, column, ref sourceTimeParts, rowIndex, rootKey),
            _ => throw new ArgumentOutOfRangeException(
                nameof(column), column.Kind, "Неизвестный тип колонки источника."),
        };
    }

    private static (object?[] Row, DateTime Time) BuildTargetRow(
        object?[] sourceValues,
        in SourceTimeParts sourceTimeParts,
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
                FillRule.SourceDateTime => MoexClickHouseTime.BuildSourceTime(in sourceTimeParts),
                // Значение уже прочитано разбором по байтам с видом «неопределённый»,
                // и помощник проставлял бы ровно его же. Берётся готовый объект:
                // распаковка с повторной упаковкой давала вторую упаковку на строку.
                FillRule.WallClock => sourceValues[column.SourceIndex],
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

    /// <summary>
    /// Момент отвергнутой строки. Вызывается только при отказе, поэтому обычный путь
    /// разбора ничего не тратит. Непрочитанный момент — не отказ: строка уже отвергнута,
    /// а неизвестный момент обход границы группы обрабатывает сам.
    /// </summary>
    private static DateTime? TryReadSourceTime(in SourceTimeParts sourceTimeParts)
    {
        try
        {
            return MoexClickHouseTime.BuildSourceTime(in sourceTimeParts);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static object? ReadMomentDate(
        ref Utf8JsonReader reader,
        SourceColumn column,
        ref SourceTimeParts sourceTimeParts,
        int rowIndex,
        string rootKey)
    {
        sourceTimeParts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(
            ref reader, rowIndex, column.Position, rootKey, out sourceTimeParts.RawDate);
        return null;
    }

    private static object? ReadMomentTime(
        ref Utf8JsonReader reader,
        SourceColumn column,
        ref SourceTimeParts sourceTimeParts,
        int rowIndex,
        string rootKey)
    {
        sourceTimeParts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(
            ref reader, rowIndex, column.Position, rootKey, out sourceTimeParts.RawTime);
        return null;
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
