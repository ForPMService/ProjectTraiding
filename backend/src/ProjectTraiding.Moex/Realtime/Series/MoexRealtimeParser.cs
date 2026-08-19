using System.Globalization;
using System.Text;
using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>
/// Разбор ответов приёма непосредственно в строки вставки по декларации. Состояния нет:
/// рабочие массивы и границы принадлежат одному вызову и не переживают страницу.
/// </summary>
public static class MoexRealtimeParser
{
    public static RealtimeParsedPage ParsePage(
        ReadOnlySpan<byte> body, MoexRealtimeSpec spec, string secid)
    {
        DateOnly? sessionDate = ParseSessionDate(body, out Exception? sessionDateFailure);

        List<object?[]> rows = [];
        DateTime firstTime = default;
        DateTime lastTime = default;
        long? lastTradeNo = null;
        long? maxTradeNo = null;
        DateTime? maxTradeNoTime = null;
        Exception? maxTradeNoTimeFailure = null;
        Exception? guardFailure = null;

        // Один рабочий массив переиспользуется всеми строками страницы. Каждая позиция
        // ниже присваивается заново, включая null, чтобы значения строк не смешивались.
        object?[] sourceValues = new object?[spec.SourceColumns.Length];

        // Повторяющийся SEQNUM снимка разбирается один раз, но проверка пустоты и формы
        // остаётся построчной: одинаковость номера — наблюдение, а не договор источника.
        long? cachedSnapshotSeqNum = null;
        DateTime? cachedSnapshotTime = null;
        Exception? cachedSnapshotFailure = null;

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
                ValidateColumns(ref reader, spec);
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
                ReadPageRows(
                    ref reader,
                    rows,
                    sourceValues,
                    spec,
                    secid,
                    sessionDate,
                    sessionDateFailure,
                    ref firstTime,
                    ref lastTime,
                    ref lastTradeNo,
                    ref maxTradeNo,
                    ref maxTradeNoTime,
                    ref maxTradeNoTimeFailure,
                    ref guardFailure,
                    ref cachedSnapshotSeqNum,
                    ref cachedSnapshotTime,
                    ref cachedSnapshotFailure);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);

        return new RealtimeParsedPage(
            rows,
            firstTime,
            lastTime,
            lastTradeNo,
            maxTradeNo,
            maxTradeNoTime,
            maxTradeNoTimeFailure,
            guardFailure);
    }

    public static List<(object?[] Row, DateTime? Begin)> ParseCandles(
        ReadOnlySpan<byte> body, MoexRealtimeSpec spec, string secid)
    {
        List<(object?[] Row, DateTime? Begin)> rows = [];
        object?[] sourceValues = new object?[spec.SourceColumns.Length];
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
                ValidateColumns(ref reader, spec);
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
                ReadCandleRows(ref reader, rows, sourceValues, spec, secid);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        return rows;
    }

    private static DateOnly? ParseSessionDate(
        ReadOnlySpan<byte> body, out Exception? failure)
    {
        ColumnAndNumbersForParsing.ExpectedSchema schema =
            ColumnAndNumbersForParsing.RealtimeDataVersionSchema;
        Utf8JsonReader reader = new(body);

        ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

        bool foundColumns = false;
        bool foundData = false;
        int rowCount = 0;
        string? tradeSessionDate = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("columns"u8))
            {
                foundColumns = true;
                ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }

                foundData = true;
                int currentRowCount = 0;
                tradeSessionDate = ReadSessionDateData(
                    ref reader, schema, ref currentRowCount);
                rowCount = currentRowCount;
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);

        if (rowCount == 0)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{schema.RootKey}] Блок dataversion не содержит ни одной строки.");
        }

        try
        {
            failure = null;
            return MoexClickHouseTime.ParseDate(tradeSessionDate);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            failure = ex;
            return null;
        }
    }

    private static string? ReadSessionDateData(
        ref Utf8JsonReader reader,
        ColumnAndNumbersForParsing.ExpectedSchema schema,
        ref int rowCount)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

        string? tradeSessionDate = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (rowCount > 0)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{schema.RootKey}] Ожидалась ровно 1 строка в dataversion, " +
                    "но найдено больше.");
            }

            for (int position = 0; position < schema.TotalColumns; position++)
            {
                if (!reader.Read())
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowCount}, " +
                        $"позиция {position}.");
                }

                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{schema.RootKey}] Короткая строка данных: ожидалось " +
                        $"{schema.TotalColumns} колонок, получено {position} " +
                        $"(строка {rowCount}).");
                }

                if (reader.TokenType == JsonTokenType.Null)
                    continue;

                switch (position)
                {
                    case 0:
                        _ = ParseHelpersUtf8.ReadInt(
                            ref reader, rowCount, position, schema.RootKey);
                        break;
                    case 1:
                        _ = ParseHelpersUtf8.ReadLong(
                            ref reader, rowCount, position, schema.RootKey);
                        break;
                    case 2:
                        // Значение отбрасывается — создавать из него строку незачем,
                        // сверка вида токена остаётся прежней.
                        ParseHelpersUtf8.ExpectString(
                            ref reader, rowCount, position, schema.RootKey);
                        break;
                    case 3:
                        tradeSessionDate = ParseHelpersUtf8.ReadString(
                            ref reader, rowCount, position, schema.RootKey);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(position), position, "Неизвестная колонка dataversion.");
                }
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{schema.RootKey}] Ожидался EndArray после " +
                    $"{schema.TotalColumns} колонок (строка {rowCount}).");
            }

            rowCount++;
        }

        return tradeSessionDate;
    }

    private static void ReadPageRows(
        ref Utf8JsonReader reader,
        List<object?[]> rows,
        object?[] sourceValues,
        MoexRealtimeSpec spec,
        string secid,
        DateOnly? sessionDate,
        Exception? sessionDateFailure,
        ref DateTime firstTime,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure,
        ref Exception? guardFailure,
        ref long? cachedSnapshotSeqNum,
        ref DateTime? cachedSnapshotTime,
        ref Exception? cachedSnapshotFailure)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", spec.RootKey);

        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, " +
                    $"получено {reader.TokenType}.");
            }

            ReadSourceValues(ref reader, sourceValues, spec, rowIndex);

            object?[] row = new object?[spec.TargetColumns.Length];
            DateTime? rowKeyTime = null;
            Exception? rowKeyTimeFailure = null;
            long? rowTradeNo = null;

            for (int i = 0; i < spec.TargetColumns.Length; i++)
            {
                TargetColumn column = spec.TargetColumns[i];
                object? value;
                Exception? valueFailure = null;

                switch (column.FillRule)
                {
                    case FillRule.TaskSecId:
                        value = secid;
                        break;
                    case FillRule.Direct:
                        value = sourceValues[column.SourceIndex];
                        break;
                    case FillRule.SourceDateTime:
                        try
                        {
                            value = MoexClickHouseTime.BuildSourceTime(
                                sourceValues[column.SourceIndex] as string,
                                sourceValues[column.SecondSourceIndex] as string);
                        }
                        catch (Exception ex) when (
                            ex is InvalidOperationException or FormatException
                                or ArgumentOutOfRangeException)
                        {
                            // Пустые значения и неверный формат дают разные отказы. Сохраняется
                            // исходное исключение, чтобы не подменить ни его тип, ни его текст.
                            valueFailure = ex;
                            value = null;
                        }
                        break;
                    case FillRule.WallClock:
                        if (spec.SourceColumns[column.SourceIndex].Kind == ColumnKind.String)
                        {
                            try
                            {
                                value = MoexClickHouseTime.ParseWallClock(
                                    sourceValues[column.SourceIndex] as string);
                            }
                            catch (Exception ex) when (
                                ex is InvalidOperationException or FormatException
                                    or ArgumentOutOfRangeException)
                            {
                                valueFailure = ex;
                                value = null;
                            }
                        }
                        else
                        {
                            value = MoexClickHouseTime.AsWallClock(
                                (DateTime?)sourceValues[column.SourceIndex]);
                        }
                        break;
                    case FillRule.Constant:
                        value = column.Constant;
                        break;
                    case FillRule.SessionDate:
                        // Отказ даты сессии занимает её прежнее место между полями строки.
                        valueFailure = sessionDateFailure;
                        value = sessionDate;
                        break;
                    case FillRule.SnapshotTimeFromSeqNum:
                        value = ReadSnapshotTime(
                            sourceValues[column.SourceIndex],
                            ref cachedSnapshotSeqNum,
                            ref cachedSnapshotTime,
                            ref cachedSnapshotFailure,
                            out valueFailure);
                        break;
                    case FillRule.ExternalSecId:
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(column), column.FillRule, "Неизвестное правило заполнения.");
                }

                if (valueFailure is null && IsRequiredValueMissing(column, value))
                {
                    valueFailure = new InvalidOperationException(
                        column.RequiredMessage
                        ?? $"Строка отвергнута: обязательная колонка {column.Name} пуста.");
                }

                guardFailure ??= valueFailure;
                row[i] = value;

                if (i == spec.KeyTimeIndex)
                {
                    rowKeyTime = value is DateTime time ? time : null;
                    rowKeyTimeFailure = valueFailure;
                }

                if (i == spec.TradeNoIndex)
                    rowTradeNo = value is long tradeNo ? tradeNo : null;
            }

            DateTime keyTime = rowKeyTime ?? default;
            if (rows.Count == 0)
                firstTime = keyTime;
            lastTime = keyTime;
            lastTradeNo = rowTradeNo;

            if (rowTradeNo is not null
                && (maxTradeNo is null || rowTradeNo.Value > maxTradeNo.Value))
            {
                maxTradeNo = rowTradeNo;
                maxTradeNoTime = rowKeyTime;
                maxTradeNoTimeFailure = rowKeyTimeFailure;
            }

            rows.Add(row);
            rowIndex++;
        }
    }

    private static void ReadCandleRows(
        ref Utf8JsonReader reader,
        List<(object?[] Row, DateTime? Begin)> rows,
        object?[] sourceValues,
        MoexRealtimeSpec spec,
        string secid)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", spec.RootKey);

        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, " +
                    $"получено {reader.TokenType}.");
            }

            ReadSourceValues(ref reader, sourceValues, spec, rowIndex);

            object?[] row = new object?[spec.TargetColumns.Length];
            for (int i = 0; i < spec.TargetColumns.Length; i++)
            {
                TargetColumn column = spec.TargetColumns[i];
                object? value = column.FillRule switch
                {
                    FillRule.TaskSecId => secid,
                    FillRule.Direct => sourceValues[column.SourceIndex],
                    FillRule.WallClock =>
                        spec.SourceColumns[column.SourceIndex].Kind == ColumnKind.String
                            ? MoexClickHouseTime.ParseWallClock(
                                sourceValues[column.SourceIndex] as string)
                            : MoexClickHouseTime.AsWallClock(
                                (DateTime?)sourceValues[column.SourceIndex]),
                    FillRule.Constant => column.Constant,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(column), column.FillRule, "Неизвестное правило заполнения."),
                };

                row[i] = value;
            }

            DateTime? begin = row[spec.KeyTimeIndex] is DateTime beginValue
                ? beginValue
                : null;
            rows.Add((row, begin));
            rowIndex++;
        }
    }

    private static void ReadSourceValues(
        ref Utf8JsonReader reader,
        object?[] sourceValues,
        MoexRealtimeSpec spec,
        int rowIndex)
    {
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
    }

    private static object? ReadValue(
        ref Utf8JsonReader reader,
        SourceColumn column,
        bool used,
        int rowIndex,
        string rootKey)
    {
        if (used)
            return ReadUsedValue(ref reader, column, rowIndex, rootKey);

        // На эту колонку не ссылается ни одна целевая колонка. Сверка вида токена остаётся —
        // она датчик дрейфа схемы источника, — но строка из значения не создаётся.
        if (column.Kind == ColumnKind.String)
        {
            ParseHelpersUtf8.ExpectString(ref reader, rowIndex, column.Position, rootKey);
            return null;
        }

        // Сегодня невостребованных колонок прочих видов нет ни в одной декларации. Ветвь
        // существует ради будущих деклараций и читает значение тем же помощником, отбрасывая
        // результат: так проверки и тексты исключений остаются буквально прежними.
        _ = ReadUsedValue(ref reader, column, rowIndex, rootKey);
        return null;
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

    private static DateTime? ReadSnapshotTime(
        object? sourceValue,
        ref long? cachedSeqNum,
        ref DateTime? cachedTime,
        ref Exception? cachedFailure,
        out Exception? failure)
    {
        long? seqNum = sourceValue is long value ? value : null;
        if (seqNum is null || seqNum.Value <= 0)
        {
            failure = new InvalidOperationException(
                "Строка стакана отвергнута: SEQNUM пуст.");
            return null;
        }

        long seqNumValue = seqNum.Value;
        if (seqNumValue < 10_000_000_000_000L || seqNumValue > 99_999_999_999_999L)
        {
            string text = seqNumValue.ToString(CultureInfo.InvariantCulture);
            failure = new InvalidOperationException(
                $"Строка стакана отвергнута: SEQNUM='{text}' не в форме YYYYMMDDHHmmss.");
            return null;
        }

        if (cachedSeqNum != seqNumValue)
        {
            cachedSeqNum = seqNumValue;
            cachedTime = null;
            cachedFailure = null;

            long rest = seqNumValue;
            int second = (int)(rest % 100);
            rest /= 100;
            int minute = (int)(rest % 100);
            rest /= 100;
            int hour = (int)(rest % 100);
            rest /= 100;
            int day = (int)(rest % 100);
            rest /= 100;
            int month = (int)(rest % 100);
            int year = (int)(rest / 100);

            try
            {
                cachedTime = new DateTime(
                    year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
            {
                cachedFailure = ex;
            }
        }

        failure = cachedFailure;
        return cachedTime;
    }

    private static bool IsRequiredValueMissing(TargetColumn column, object? value)
    {
        return column.Required
            && (value is null || value is string text && string.IsNullOrWhiteSpace(text));
    }

    private static void ValidateColumns(ref Utf8JsonReader reader, MoexRealtimeSpec spec)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "columns", spec.RootKey);

        int position = 0;
        int expectedIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (expectedIndex < spec.SourceColumns.Length
                && position == spec.SourceColumns[expectedIndex].Position)
            {
                SourceColumn column = spec.SourceColumns[expectedIndex];
                if (reader.TokenType != JsonTokenType.String
                    || !reader.ValueTextEquals(column.Name))
                {
                    string actual = reader.TokenType == JsonTokenType.String
                        ? reader.GetString() ?? "<null>"
                        : $"<{reader.TokenType}>";
                    string expected = Encoding.UTF8.GetString(column.Name);

                    throw new MoexSchemaMismatchException(
                        $"[{spec.RootKey}] Колонка не совпала на позиции {position}: " +
                        $"ожидалось '{expected}', получено '{actual}'.");
                }

                expectedIndex++;
            }

            position++;
        }

        if (position != spec.SourceColumns.Length)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{spec.RootKey}] Количество колонок не совпадает: " +
                $"ожидалось {spec.SourceColumns.Length}, получено {position}.");
        }

        if (expectedIndex != spec.SourceColumns.Length)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{spec.RootKey}] Не все ожидаемые колонки найдены: " +
                $"ожидалось {spec.SourceColumns.Length}, проверено {expectedIndex}.");
        }
    }
}
