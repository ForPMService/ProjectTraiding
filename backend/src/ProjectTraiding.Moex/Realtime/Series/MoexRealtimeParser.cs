using System.Globalization;
using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
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
        // Дата сессии читается тем же проходом, что и таблица: источник отдаёт блок версии
        // данных после таблицы, и отдельный проход ради него протаскивал читатель через всю
        // таблицу впустую. Значения ниже заполняются, когда проход дойдёт до этого блока.
        DateOnly? sessionDate = null;
        Exception? sessionDateFailure = null;

        List<object?[]> rows = [];
        DateTime firstTime = default;
        DateTime lastTime = default;
        long? lastTradeNo = null;
        long? maxTradeNo = null;
        DateTime? maxTradeNoTime = null;
        Exception? maxTradeNoTimeFailure = null;
        Exception? guardFailure = null;

        // Первый отказ первой строки среди колонок до колонки даты сессии. Нужен, чтобы
        // восстановить прежний порядок отсеянного отказа после разбора всего тела.
        Exception? firstRowFailureBeforeSessionDate = null;

        // Один рабочий массив переиспользуется всеми строками страницы. Каждая позиция
        // ниже присваивается заново, включая null, чтобы значения строк не смешивались.
        object?[] sourceValues = new object?[spec.SourceColumns.Length];

        // Повторяющийся SEQNUM снимка разбирается один раз, но проверка пустоты и формы
        // остаётся построчной: одинаковость номера — наблюдение, а не договор источника.
        long? cachedSnapshotSeqNum = null;
        DateTime? cachedSnapshotTime = null;
        Exception? cachedSnapshotFailure = null;

        ColumnAndNumbersForParsing.ExpectedSchema dataVersionSchema =
            ColumnAndNumbersForParsing.RealtimeDataVersionSchema;

        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);

        bool foundTable = false;
        bool foundDataVersion = false;
        bool foundColumns = false;
        bool foundData = false;

        // Свойства верхнего уровня разбираются по мере встречи, в любом порядке: от порядка
        // блоков в ответе разбор не зависит, и требованием к источнику он не становится.
        //
        // Обход прекращается, как только разобраны оба нужных блока. Прежние два прохода
        // тоже заканчивались на своём блоке и хвост ответа за ним не читали: без этого
        // условия повреждённый хвост после двух исправных блоков начал бы отвергать ответ,
        // которого прежний разбор даже не касался, а на диагностических ответах читатель
        // зря протаскивался бы через секцию доходностей.
        while (!(foundTable && foundDataVersion) && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            // Разбирается только первое вхождение каждого блока. Прежний вход в корневой
            // блок возвращал управление на первом совпадении и до повторов не доходил;
            // без этих признаков одиночный проход разобрал бы вторую таблицу в те же строки
            // и подменил бы дату сессии вторым блоком версии данных.
            //
            // Остановка обхода выше эти признаки не заменяет: повтор может встретиться
            // раньше, чем найден второй нужный блок, — например при порядке
            // «таблица, таблица, версия данных».
            if (!foundDataVersion && reader.ValueTextEquals(dataVersionSchema.RootKey))
            {
                foundDataVersion = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, dataVersionSchema.RootKey);
                sessionDate = ReadDataVersionBlock(
                    ref reader, dataVersionSchema, out sessionDateFailure);
                continue;
            }

            if (foundTable || !reader.ValueTextEquals(spec.RootKey))
            {
                reader.Skip();
                continue;
            }

            foundTable = true;
            ParseHelpersUtf8.ExpectObjectStart(ref reader, spec.RootKey);

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
                    ReadPageRows(
                        ref reader,
                        rows,
                        sourceValues,
                        spec,
                        secid,
                        ref firstTime,
                        ref lastTime,
                        ref lastTradeNo,
                        ref maxTradeNo,
                        ref maxTradeNoTime,
                        ref maxTradeNoTimeFailure,
                        ref guardFailure,
                        ref firstRowFailureBeforeSessionDate,
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
        }

        // Отсутствующий блок проверяется в прежнем порядке: сначала блок версии данных,
        // затем таблица. Прежде первый проход искал блок версии данных и отказывал первым.
        if (!foundDataVersion)
            ParseHelpersUtf8.RootKeyMissing(dataVersionSchema.RootKey);

        if (!foundTable)
            ParseHelpersUtf8.RootKeyMissing(spec.RootKey);

        // Дата сессии известна только после разбора блока версии данных, а строки к этому
        // моменту уже собраны. Проставляется по ссылкам на готовые строки: повторного
        // разбора тела нет, есть один проход по списку.
        if (spec.SessionDateIndex >= 0 && sessionDate is not null)
        {
            for (int i = 0; i < rows.Count; i++)
                rows[i][spec.SessionDateIndex] = sessionDate;
        }

        // Прежний порядок отсеянного отказа. Раньше отказ даты сессии участвовал в отборе
        // на месте своей колонки в каждой строке, поэтому побеждал всё, что стоит после неё,
        // и уступал тому, что стоит до неё в первой строке. Это правило и восстанавливается.
        if (sessionDateFailure is not null && rows.Count > 0 && spec.SessionDateIndex >= 0)
            guardFailure = firstRowFailureBeforeSessionDate ?? sessionDateFailure;

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

    /// <summary>
    /// Читает блок версии данных с текущей позиции: читатель уже стоит на его StartObject.
    /// Возвращает дату сессии; отказ разбора самой даты не бросается, а отдаётся наружу —
    /// он занимает место колонки даты сессии в строке, как и прежде.
    /// </summary>
    private static DateOnly? ReadDataVersionBlock(
        ref Utf8JsonReader reader,
        ColumnAndNumbersForParsing.ExpectedSchema schema,
        out Exception? failure)
    {
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
        ref DateTime firstTime,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure,
        ref Exception? guardFailure,
        ref Exception? firstRowFailureBeforeSessionDate,
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
                        // Дата сессии на этот момент ещё не прочитана: её блок источник
                        // отдаёт после таблицы. Место колонки занимает пустое значение,
                        // а дата и её отказ проставляются после разбора всего тела.
                        // Обязательной эта колонка не объявлена ни в одной декларации,
                        // поэтому пустое значение здесь отказа не порождает.
                        value = null;
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

                // Отказы первой строки до колонки даты сессии запоминаются отдельно:
                // из них и из отказа даты сессии восстанавливается прежний порядок отбора.
                //
                // Первая строка определяется по числу уже собранных строк, а не по счётчику
                // строк этого вызова: счётчик местный и обнуляется на каждой секции 'data',
                // а секций внутри блока может встретиться несколько — разбор их допускает.
                // Соседняя строка метода уже определяет первую строку страницы этим же
                // способом: по нулевому числу собранных строк выставляется время первой строки.
                if (rows.Count == 0 && spec.SessionDateIndex >= 0 && i < spec.SessionDateIndex)
                    firstRowFailureBeforeSessionDate ??= valueFailure;

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

}
