
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using System.Collections.Immutable;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParseHelpersUtf8
    {
        // ═══════════════════════════════════════════════════════════
        // Structural failure helper (Phase 8-A, Lock §10)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Бросает MoexSchemaMismatchException для structural failure парсера.
        /// Помечен [DoesNotReturn] чтобы компилятор не требовал unreachable return после вызова.
        /// Используется парсерами для: missing root key, missing columns block, missing data block,
        /// data before columns, missing cursor block, wrong token type structural. Lock §10.
        /// </summary>
        [DoesNotReturn]
        internal static void SchemaMismatch(string message)
        {
            throw new MoexSchemaMismatchException(message: message);
        }

        internal static void SkipToRootObject(
            ref Utf8JsonReader reader,
            string rootKey)
        {
            // Найти первый объект ответа, пропуская предшествующие токены.
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                    break;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
                SchemaMismatch("Ожидался объект ответа MOEX.");

            // Внутри корневого объекта JSON — ищем свойство rootKey
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    SchemaMismatch($"MOEX ответ не содержит корневой ключ '{rootKey}'.");

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals(rootKey))
                {
                    ReadAndExpect(ref reader, JsonTokenType.StartObject, rootKey, rootKey);
                    return;
                }

                reader.Skip();
            }

            SchemaMismatch($"MOEX ответ не содержит корневой ключ '{rootKey}'.");
        }

        // ═══════════════════════════════════════════════════════════
        // Валидация columns[]
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Проверяет плотный массив входных колонок на точное совпадение имён и порядка.
        /// </summary>
        internal static void ValidateInputColumnsUtf8(
            ref Utf8JsonReader reader,
            ImmutableArray<string> columns,
            string rootKey)
        {
            try
            {
                ReadAndExpect(ref reader, JsonTokenType.StartArray, "columns", rootKey);

                for (int position = 0; position < columns.Length; position++)
                {
                    if (!reader.Read())
                    {
                        SchemaMismatch(
                            $"[{rootKey}] Заголовок columns завершился до колонки {position}; " +
                            $"ожидалось {columns.Length} колонок.");
                    }

                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        SchemaMismatch(
                            $"[{rootKey}] Недостаточно колонок: ожидалось {columns.Length}, " +
                            $"получено {position}.");
                    }

                    if (reader.TokenType != JsonTokenType.String)
                    {
                        SchemaMismatch(
                            $"[{rootKey}] Ожидалась строковая колонка '{columns[position]}' " +
                            $"на позиции {position}, получено <{reader.TokenType}>.");
                    }

                    if (!reader.ValueTextEquals(columns[position]))
                    {
                        string actual = reader.GetString() ?? "<null>";
                        SchemaMismatch(
                            $"[{rootKey}] Колонка не совпала на позиции {position}: " +
                            $"ожидалось '{columns[position]}', получено '{actual}'.");
                    }
                }

                if (!reader.Read())
                {
                    SchemaMismatch(
                        $"[{rootKey}] Заголовок columns не завершился EndArray " +
                        $"после {columns.Length} колонок.");
                }

                if (reader.TokenType != JsonTokenType.EndArray)
                {
                    SchemaMismatch(
                        $"[{rootKey}] Лишний элемент в columns на позиции {columns.Length}: " +
                        $"получено <{reader.TokenType}>, ожидалось окончание массива.");
                }
            }
            catch (JsonException exception)
            {
                SchemaMismatch(
                    $"[{rootKey}] Некорректная структура JSON в заголовке columns: " +
                    exception.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Валидация структуры
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Проверка что парсер нашёл обязательные секции columns и data.
        /// SkipToRootObject уже проверил наличие rootKey — здесь не дублируем.
        /// </summary>
        internal static void ValidateStructure(
            bool foundColumns,
            bool foundData,
            string rootKey)
        {
            if (!foundColumns)
                SchemaMismatch(
                    $"[{rootKey}] Блок не содержит секцию 'columns'.");

            if (!foundData)
                SchemaMismatch(
                    $"[{rootKey}] Блок не содержит секцию 'data'.");
        }

        // ═══════════════════════════════════════════════════════════
        // Чтение токенов
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Прочитать следующий токен и проверить что он нужного типа.
        /// </summary>
        internal static void ReadAndExpect(
            ref Utf8JsonReader reader,
            JsonTokenType expectedType,
            string context,
            string rootKey)
        {
            if (!reader.Read() || reader.TokenType != expectedType)
                SchemaMismatch(
                    $"[{rootKey}] Ожидался {expectedType} для '{context}', " +
                    $"получено {reader.TokenType}.");
        }

        internal static int? ReadInt(
            ref Utf8JsonReader reader, out string? error)
        {
            error = null;

            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetInt32(out int value))
                return value;

            error = "Ожидалось значение Int32.";
            return null;
        }

        internal static long? ReadLong(
            ref Utf8JsonReader reader, out string? error)
        {
            error = null;

            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetInt64(out long value))
                return value;

            error = "Ожидалось значение Int64.";
            return null;
        }

        internal static double? ReadDouble(
            ref Utf8JsonReader reader, out string? error)
        {
            error = null;

            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetDouble(out double value))
                return value;

            error = "Ожидалось значение Double.";
            return null;
        }

        /// <summary>
        /// Предельная длина значения, которое сверяется на буфере стека. Значения источника
        /// короткие; всё, что длиннее, берётся из пула, чтобы не раздувать кадр стека.
        /// </summary>
        private const int StackStringCheckLimit = 128;

        internal static void ExpectString(
            ref Utf8JsonReader reader,
            out string? error)
        {
            error = null;

            if (reader.TokenType == JsonTokenType.Null)
                return;

            if (reader.TokenType != JsonTokenType.String)
            {
                error = "Ожидалось строковое значение.";
                return;
            }

            int encodedLength = reader.HasValueSequence
                ? checked((int)reader.ValueSequence.Length)
                : reader.ValueSpan.Length;

            if (encodedLength <= StackStringCheckLimit)
            {
                Span<char> buffer = stackalloc char[StackStringCheckLimit];
                _ = reader.CopyString(buffer);
                return;
            }

            char[] rented = ArrayPool<char>.Shared.Rent(encodedLength);
            try
            {
                _ = reader.CopyString(rented);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        /// <summary>
        /// Прочитать string из текущего токена.
        /// </summary>
        internal static string? ReadString(
            ref Utf8JsonReader reader,
            out string? error)
        {
            error = null;

            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                error = "Ожидалось строковое значение.";
                return null;
            }

            return reader.GetString();
        }

        internal static DateOnly? ReadDate(
            ref Utf8JsonReader reader,
            out string? raw,
            out string? error)
        {
            raw = null;
            error = null;

            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                error = "Ожидалась дата в формате yyyy-MM-dd.";
                return null;
            }

            string? text = reader.GetString();
            if (text is not null && text.Length == 10 &&
                DateOnly.TryParseExact(
                    text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly value))
                return value;

            raw = text;
            error = "Некорректная дата; ожидался формат yyyy-MM-dd.";
            return null;
        }

        internal static TimeOnly? ReadTime(
            ref Utf8JsonReader reader,
            out string? raw,
            out string? error)
        {
            raw = null;
            error = null;

            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                error = "Ожидалось время в формате HH:mm:ss.";
                return null;
            }

            string? text = reader.GetString();
            if (text is not null && text.Length == 8 &&
                TimeOnly.TryParseExact(
                    text, "HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out TimeOnly value))
                return value;

            raw = text;
            error = "Некорректное время; ожидался формат HH:mm:ss.";
            return null;
        }

        internal static DateTime? ReadDateTime(
            ref Utf8JsonReader reader,
            out bool wasNull,
            out string? error)
        {
            wasNull = reader.TokenType == JsonTokenType.Null;
            error = null;

            if (wasNull)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                error = "Ожидался момент yyyy-MM-dd HH:mm:ss.";
                return null;
            }

            string? text = reader.GetString();
            if (text == "")
                return null;  // Не JSON null: wasNull остаётся false.

            if (text is not null && text.Length == 19 &&
                DateTime.TryParseExact(
                    text, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out DateTime parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            }

            error = "Некорректный момент; ожидался yyyy-MM-dd HH:mm:ss.";
            return null;
        }

        internal static ulong? ReadSystime(
            ref Utf8JsonReader reader, out string? error)
        {
            DateTime? value = ReadDateTime(
                ref reader, out _, out error);

            if (error is not null || !value.HasValue)
                return null;

            try
            {
                return CandleDateTime64.ToUnixWholeSeconds(value.Value);
            }
            catch (ArgumentException exception)
            {
                error = $"Недопустимое время SYSTIME: {exception.Message}";
                return null;
            }
            catch (OverflowException exception)
            {
                error = $"Недопустимое время SYSTIME: {exception.Message}";
                return null;
            }
        }

/// <summary>
/// Перейти к очередному значению текущей строки.
/// columnIndex — число предпринятых чтений; индекс ошибочной колонки = columnIndex - 1.
/// При ошибке оставить reader на проблемном токене (или последней позиции),
/// чтобы вызывающий код один раз вызвал RejectRow.
/// </summary>
private static bool TryReadNextScalar(
    ref Utf8JsonReader reader,
    ref int columnIndex,
    out string? error)
{
    columnIndex++;
    error = null;

    if (!reader.Read())
    {
        error = "Не удалось прочитать значение колонки.";
        return false;
    }

    if (reader.TokenType == JsonTokenType.Null)
    {
        error = "JSON null недопустим.";
        return false;
    }

    if (reader.TokenType == JsonTokenType.EndArray)
    {
        error = "В строке меньше значений, чем ожидается.";
        return false;
    }

    if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject
        or JsonTokenType.EndObject or JsonTokenType.PropertyName)
    {
        error = "Ожидалось скалярное значение колонки.";
        return false;
    }

    return true;
}

internal static bool TryNextInt(
    ref Utf8JsonReader reader, ref int columnIndex,
    out int? value, out string? error)
{
    value = null;
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    value = ReadInt(ref reader, out error);
    return error is null;
}

internal static bool TryNextLong(
    ref Utf8JsonReader reader, ref int columnIndex,
    out long? value, out string? error)
{
    value = null;
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    value = ReadLong(ref reader, out error);
    return error is null;
}

internal static bool TryNextDouble(
    ref Utf8JsonReader reader, ref int columnIndex,
    out double? value, out string? error)
{
    value = null;
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    value = ReadDouble(ref reader, out error);
    return error is null;
}

internal static bool TryNextString(
    ref Utf8JsonReader reader, ref int columnIndex,
    out string? value, out string? error)
{
    value = null;
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    value = ReadString(ref reader, out error);
    return error is null;
}

/// <summary>
/// Проверить строковый токен без выделения строки результата.
/// Для secid из ответа, когда в результирующее поле записывается taskSecId.
/// </summary>
internal static bool TryNextStringToken(
    ref Utf8JsonReader reader, ref int columnIndex,
    out string? error)
{
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    ExpectString(ref reader, out error);
    return error is null;
}

internal static bool TryNextDate(
    ref Utf8JsonReader reader, ref int columnIndex,
    out DateOnly? value, out string? error)
{
    value = null;
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    value = ReadDate(ref reader, out _, out error);
    return error is null;
}

internal static bool TryNextTime(
    ref Utf8JsonReader reader, ref int columnIndex,
    out TimeOnly? value, out string? error)
{
    value = null;
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    value = ReadTime(ref reader, out _, out error);
    return error is null;
}

internal static bool TryNextSystime(
    ref Utf8JsonReader reader, ref int columnIndex,
    out ulong? value, out string? error)
{
    value = null;
    if (!TryReadNextScalar(ref reader, ref columnIndex, out error))
        return false;
    value = ReadSystime(ref reader, out error);
    return error is null;
}

/// <summary>
/// Завершает строку по схеме. Лишние ячейки — отказ строки; расхождение парсера со схемой — ошибка программы.
/// </summary>
internal static bool TryRowEnd(
    ref Utf8JsonReader reader, ref int columnIndex, int columnCount, out string? error)
{
    if (columnIndex != columnCount)
        throw new InvalidOperationException(
            $"Парсер прочитал {columnIndex} значений, схема содержит {columnCount}.");

    error = null;
    if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
        return true;

    columnIndex++;
    error = $"В строке больше значений, чем {columnCount}.";
    return false;
}

internal static void RejectRow(
    ref Utf8JsonReader reader,
    int rowDepth,
    HistoricalReadState state,
    int rowIndex,
    DateTime? sourceTime,
    string reason,
    int? columnIndex = null,
    string? columnName = null)
{
    // Регистрируем отказ ровно один раз до перемещения ридера.
    state.RecordSkipped(rowIndex, sourceTime, reason, columnIndex, columnName);

    if (reader.TokenType == JsonTokenType.EndArray &&
        reader.CurrentDepth == rowDepth)
        return;

    while (reader.Read())
    {
        if (reader.TokenType == JsonTokenType.EndArray &&
            reader.CurrentDepth == rowDepth)
            return;
    }

    throw new JsonException("Не найден конец отклонённой строки.");
}


    internal static void AppendRow<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.All)] TRow>(
    ProtobufBatchWriter<TRow> writer,
    in TRow row)
    where TRow : struct
{
    writer.Append(in row);
}    

        // ═══════════════════════════════════════════════════════════
        // Cursor-парсер — чтение из текущего reader (Phase 3)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Читает cursor-блок (columns + data) из текущей позиции reader.
        /// 
        /// Вызывающий код уже прошёл верхний уровень JSON и встретил PropertyName
        /// совпадающее с постоянным ключом "data.cursor". Reader стоит на этом PropertyName.
        /// 
        /// Метод:
        /// 1. Читает StartObject
        /// 2. Внутри объекта ищет "columns" и "data" (в порядке columns → data)
        /// 3. Возвращает заполненный PaginationCursorDTO
        /// 
        /// Если cursor-блок не найден в JSON — не бросает, вызывающий парсер
        /// сам решает как обрабатывать отсутствие cursor.
        /// Этот метод вызывается только когда PropertyName уже совпало.
        /// </summary>
        internal static PaginationCursorDTO ReadCursorRootObject(
            ref Utf8JsonReader reader)
        {
            const string rootKey = "data.cursor";
            ReadAndExpect(ref reader, JsonTokenType.StartObject, rootKey, rootKey);

            bool foundColumns = false;
            bool foundData = false;
            int? index = null, total = null, pageSize = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("columns"u8))
                {
                    foundColumns = true;
                    ValidateInputColumnsUtf8(ref reader, MoexColumnSets.Cursor, rootKey);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        SchemaMismatch(
                            $"[{rootKey}] Секция 'data' встретилась до 'columns'. " +
                            $"Порядок columns → data обязателен.");

                    foundData = true;
                    ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", rootKey);

                    bool foundDataEnd = false;
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                        {
                            foundDataEnd = true;
                            break;
                        }

                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            SchemaMismatch(
                                $"[{rootKey}] Ожидалась строка-массив в data, " +
                                $"получено {reader.TokenType}.");
                        }

                        if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
                            SchemaMismatch("[data.cursor] Недостаточно значений строки курсора: INDEX.");
                        int? rowIndexValue = ReadInt(ref reader, out string? error);
                        if (error is not null)
                            SchemaMismatch($"[data.cursor] INDEX: {error}");

                        if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
                            SchemaMismatch("[data.cursor] Недостаточно значений строки курсора: TOTAL.");
                        int? rowTotal = ReadInt(ref reader, out error);
                        if (error is not null)
                            SchemaMismatch($"[data.cursor] TOTAL: {error}");

                        if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
                            SchemaMismatch("[data.cursor] Недостаточно значений строки курсора: PAGESIZE.");
                        int? rowPageSize = ReadInt(ref reader, out error);
                        if (error is not null)
                            SchemaMismatch($"[data.cursor] PAGESIZE: {error}");

                        ReadAndExpect(ref reader, JsonTokenType.EndArray, "строка курсора", rootKey);

                        if (rowIndexValue is int indexValue)
                            index = indexValue;
                        if (rowTotal is int totalValue)
                            total = totalValue;
                        if (rowPageSize is int pageSizeValue)
                            pageSize = pageSizeValue;

                    }

                    if (!foundDataEnd)
                    {
                        SchemaMismatch($"[{rootKey}] Ожидался EndArray у секции data.");
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            ValidateStructure(foundColumns, foundData, rootKey);

            return new PaginationCursorDTO
            {
                Index    = index,
                Total    = total,
                PageSize = pageSize,
            };
        }

    }
}
