
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Collections.Immutable;
using System.Buffers.Text;
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

        // ═══════════════════════════════════════════════════════════
        // Навигация к RootKey (A1)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// На верхнем уровне JSON ищет свойство с именем rootKey,
        /// пропуская всё остальное через Skip().
        /// 
        /// После вызова reader стоит на StartObject внутри rootKey.
        /// 
        /// Бросает MoexSchemaMismatchException (Lock §10) если:
        /// — rootKey не найден до конца JSON;
        /// — значение rootKey не является объектом.
        /// </summary>
        /// <summary>
        /// Входит во внешний объект ответа. Отдельный метод нужен одиночному проходу,
        /// который дальше сам разбирает свойства верхнего уровня по мере встречи.
        /// </summary>
        internal static void EnterResponseObject(ref Utf8JsonReader reader)
        {
            // Пройти до StartObject верхнего уровня
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                    break;
            }
        }

        /// <summary>
        /// Сверяет, что за именем блока следует объект. Текст отказа объявлен здесь
        /// и в других местах не дублируется.
        /// </summary>
        internal static void ExpectObjectStart(ref Utf8JsonReader reader, string rootKey)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                SchemaMismatch(
                    $"[{rootKey}] Ожидался объект (StartObject), " +
                    $"получено {reader.TokenType}.");
            }
        }

        /// <summary>
        /// Текст отказа об отсутствующем корневом ключе. Объявлен здесь и не дублируется.
        /// </summary>
        internal static void RootKeyMissing(string rootKey)
        {
            SchemaMismatch($"MOEX ответ не содержит корневой ключ '{rootKey}'.");
        }

        internal static void SkipToRootObject(
            ref Utf8JsonReader reader,
            string rootKey)
        {
            EnterResponseObject(ref reader);

            // Внутри корневого объекта JSON — ищем свойство rootKey
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    RootKeyMissing(rootKey);

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                // Сравнение с именем в виде строки перекодирует короткое имя во временный
                // буфер на стеке и кучу не задействует. Прежний приём создавал массив байтов
                // на каждый разбор страницы. Раскодирование экранированных имён свойств
                // обе перегрузки выполняют одинаково.
                if (reader.ValueTextEquals(rootKey))
                {
                    ExpectObjectStart(ref reader, rootKey);
                    return;
                }

                reader.Skip();
            }

            RootKeyMissing(rootKey);
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
        // Универсальное чтение строки данных (A3 + A4)
        // ═══════════════════════════════════════════════════════════

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

        /// <summary>
        /// Прочитать double из текущего токена.
        /// </summary>
        internal static double ReadDouble(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался Number в строке {rowIndex}, колонка {columnIndex}, " +
                    $"получено {reader.TokenType}.");

            return reader.GetDouble();
        }

        /// <summary>
        /// Прочитать int из текущего токена.
        /// </summary>
        internal static int ReadInt(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался Number (int) в строке {rowIndex}, колонка {columnIndex}, " +
                    $"получено {reader.TokenType}.");

            return reader.GetInt32();
        }

        /// <summary>
        /// Прочитать long из текущего токена.
        /// </summary>
        internal static long ReadLong(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался Number (long) в строке {rowIndex}, колонка {columnIndex}, " +
                    $"получено {reader.TokenType}.");

            return reader.GetInt64();
        }

        /// <summary>
        /// Предельная длина значения, которое сверяется на буфере стека. Значения источника
        /// короткие; всё, что длиннее, берётся из пула, чтобы не раздувать кадр стека.
        /// </summary>
        private const int StackStringCheckLimit = 128;

        /// <summary>
        /// Общая сверка вида токена. Объявлена отдельно, чтобы текст отказа существовал
        /// в одном месте и не мог разойтись между чтением строки и её проверкой.
        /// </summary>
        private static void ThrowIfNotString(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался String в строке {rowIndex}, колонка {columnIndex}, " +
                    $"получено {reader.TokenType}.");
        }

        /// <summary>
        /// Проверяет значение строковой колонки, не создавая строку.
        ///
        /// Нужен колонкам, значение которых не попадает ни в одну целевую колонку. Сверяется
        /// не только вид токена: значение раскодируется в собственный буфер, поэтому проверка
        /// кодировки сохраняется целиком. Прежний путь через GetString отвергал недопустимые
        /// последовательности UTF-8 и непарные суррогаты; CopyString отвергает их так же,
        /// но строку в куче не создаёт. Выбрасывать саму проверку было бы сменой поведения:
        /// повреждённое значение перестало бы отвергаться.
        /// </summary>
        internal static void ExpectString(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            ThrowIfNotString(ref reader, rowIndex, columnIndex, rootKey);

            // Раскодированное значение никогда не длиннее закодированного, поэтому длины
            // исходного представления как размера буфера достаточно.
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
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            ThrowIfNotString(ref reader, rowIndex, columnIndex, rootKey);
            return reader.GetString();
        }

        internal static int? ReadNextNullableInt(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnIndex)
        {
            if (!ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex))
                return null;

            return ReadInt(ref reader, rowIndex, columnIndex, rootKey);
        }

        internal static long? ReadNextNullableLong(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnIndex)
        {
            if (!ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex))
                return null;

            return ReadLong(ref reader, rowIndex, columnIndex, rootKey);
        }

        internal static double? ReadNextNullableDouble(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnIndex)
        {
            if (!ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex))
                return null;

            return ReadDouble(ref reader, rowIndex, columnIndex, rootKey);
        }

        internal static string? ReadNextNullableString(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnIndex)
        {
            if (!ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex))
                return null;

            return ReadString(ref reader, rowIndex, columnIndex, rootKey);
        }

        internal static DateOnly? ReadNextMomentDate(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey,
            out string? raw)
        {
            raw = null;
            if (!ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex))
                return null;

            return ReadMomentDateUtf8(ref reader, rowIndex, columnIndex, rootKey, out raw);
        }

        internal static TimeOnly? ReadNextMomentTime(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey,
            out string? raw)
        {
            raw = null;
            if (!ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex))
                return null;

            return ReadMomentTimeUtf8(ref reader, rowIndex, columnIndex, rootKey, out raw);
        }

        internal static DateTime? ReadNextNullableDateTime(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnIndex,
            out bool wasNull)
        {
            wasNull = !ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex);
            return wasNull ? null : ReadDateTimeUtf8(ref reader, rowIndex, columnIndex, rootKey);
        }

        internal static void ExpectNextNullableString(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnIndex)
        {
            if (ReadNextScalar(ref reader, rowIndex, rootKey, columnIndex))
                ExpectString(ref reader, rowIndex, columnIndex, rootKey);
        }

        private static bool ReadNextScalar(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnIndex)
        {
            if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
            {
                SchemaMismatch(
                    $"[{rootKey}] Недостаточно значений в строке {rowIndex}, " +
                    $"колонка {columnIndex}.");
            }

            if (reader.TokenType is JsonTokenType.StartArray
                or JsonTokenType.StartObject
                or JsonTokenType.EndObject)
            {
                SchemaMismatch(
                    $"[{rootKey}] Недопустимая структура значения в строке {rowIndex}, " +
                    $"колонка {columnIndex}.");
            }

            return reader.TokenType != JsonTokenType.Null;
        }

        internal static void ExpectRowEnd(
            ref Utf8JsonReader reader,
            int rowIndex,
            string rootKey,
            int columnCount)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                SchemaMismatch(
                    $"[{rootKey}] Ожидался EndArray после {columnCount} колонок " +
                    $"(строка {rowIndex}), получено {reader.TokenType}.");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // DateTime без аллокаций (A5)
        // ═══════════════════════════════════════════════════════════

        internal static DateOnly? ReadMomentDateUtf8(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey,
            out string? raw)
        {
            raw = null;
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался String (moment date) в строке {rowIndex}, " +
                    $"колонка {columnIndex}, получено {reader.TokenType}.");

            Span<byte> sequenceBuffer = stackalloc byte[10];
            scoped ReadOnlySpan<byte> span;
            if (reader.HasValueSequence)
            {
                if (reader.ValueSequence.Length != 10)
                {
                    raw = reader.GetString();
                    return null;
                }

                reader.ValueSequence.CopyTo(sequenceBuffer);
                span = sequenceBuffer;
            }
            else
            {
                span = reader.ValueSpan;
                if (span.Length != 10)
                {
                    raw = reader.GetString();
                    return null;
                }
            }

            if (!Utf8Parser.TryParse(span.Slice(0, 4), out int year, out int yearBytes)
                || yearBytes != 4
                || span[4] != (byte)'-'
                || !Utf8Parser.TryParse(span.Slice(5, 2), out int month, out int monthBytes)
                || monthBytes != 2
                || span[7] != (byte)'-'
                || !Utf8Parser.TryParse(span.Slice(8, 2), out int day, out int dayBytes)
                || dayBytes != 2)
            {
                raw = reader.GetString();
                return null;
            }

            try
            {
                return new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                raw = reader.GetString();
                return null;
            }
        }

        internal static TimeOnly? ReadMomentTimeUtf8(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey,
            out string? raw)
        {
            raw = null;
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался String (moment time) в строке {rowIndex}, " +
                    $"колонка {columnIndex}, получено {reader.TokenType}.");

            Span<byte> sequenceBuffer = stackalloc byte[8];
            scoped ReadOnlySpan<byte> span;
            if (reader.HasValueSequence)
            {
                if (reader.ValueSequence.Length != 8)
                {
                    raw = reader.GetString();
                    return null;
                }

                reader.ValueSequence.CopyTo(sequenceBuffer);
                span = sequenceBuffer;
            }
            else
            {
                span = reader.ValueSpan;
                if (span.Length != 8)
                {
                    raw = reader.GetString();
                    return null;
                }
            }

            if (!Utf8Parser.TryParse(span.Slice(0, 2), out int hour, out int hourBytes)
                || hourBytes != 2
                || span[2] != (byte)':'
                || !Utf8Parser.TryParse(span.Slice(3, 2), out int minute, out int minuteBytes)
                || minuteBytes != 2
                || span[5] != (byte)':'
                || !Utf8Parser.TryParse(span.Slice(6, 2), out int second, out int secondBytes)
                || secondBytes != 2)
            {
                raw = reader.GetString();
                return null;
            }

            try
            {
                return new TimeOnly(hour, minute, second);
            }
            catch (ArgumentOutOfRangeException)
            {
                raw = reader.GetString();
                return null;
            }
        }

        /// <summary>
        /// Формат MOEX: "yyyy-MM-dd HH:mm:ss" — ровно 19 байт UTF-8.
        /// Парсим через Utf8Parser.TryParse по ValueSpan без GetString().
        /// 
        /// Убирает string-аллокации на каждую дату.
        /// При 2 годах минутных свечей — это 30+ млн аллокаций меньше.
        /// 
        /// Если токен не String — бросает ошибку.
        /// Если формат не распознан — возвращает null (MOEX может отдать пустую строку).
        /// </summary>
        internal static DateTime? ReadDateTimeUtf8(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался String (datetime) в строке {rowIndex}, колонка {columnIndex}, " +
                    $"получено {reader.TokenType}.");

            // "yyyy-MM-dd HH:mm:ss" = 19 байт
            //
            // Сплошной буфер отдаёт значение без копии. Разорванный буфер сегодня не
            // встречается — все обращения идут по сплошному входу, — но встретится при
            // переходе на потоковое чтение, и тогда копия на стеке избавит от массива
            // в куче на каждую дату. Длина известна заранее, поэтому размер копии
            // постоянен, а всё, что в него не укладывается, отбрасывается сразу.
            Span<byte> sequenceBuffer = stackalloc byte[19];
            scoped ReadOnlySpan<byte> span;
            if (reader.HasValueSequence)
            {
                if (reader.ValueSequence.Length != 19)
                    return null;

                reader.ValueSequence.CopyTo(sequenceBuffer);
                span = sequenceBuffer;
            }
            else
            {
                span = reader.ValueSpan;
                if (span.Length != 19)
                    return null;
            }

            // Парсим компоненты напрямую из UTF-8 байт.
            // Формат фиксированный — позиции гарантированы:
            // 0123456789012345678
            // yyyy-MM-dd HH:mm:ss

            if (!Utf8Parser.TryParse(span.Slice(0, 4), out int year, out _)
                || span[4] != (byte)'-'
                || !Utf8Parser.TryParse(span.Slice(5, 2), out int month, out _)
                || span[7] != (byte)'-'
                || !Utf8Parser.TryParse(span.Slice(8, 2), out int day, out _)
                || span[10] != (byte)' '
                || !Utf8Parser.TryParse(span.Slice(11, 2), out int hour, out _)
                || span[13] != (byte)':'
                || !Utf8Parser.TryParse(span.Slice(14, 2), out int minute, out _)
                || span[16] != (byte)':'
                || !Utf8Parser.TryParse(span.Slice(17, 2), out int second, out _))
            {
                return null;
            }

            try
            {
                return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
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

                    int rowIndex = 0;
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

                        int? rowIndexValue = ReadNextNullableInt(ref reader, rowIndex, rootKey, 0);
                        int? rowTotal = ReadNextNullableInt(ref reader, rowIndex, rootKey, 1);
                        int? rowPageSize = ReadNextNullableInt(ref reader, rowIndex, rootKey, 2);
                        ExpectRowEnd(ref reader, rowIndex, rootKey, MoexColumnSets.Cursor.Length);

                        if (rowIndexValue is int indexValue)
                            index = indexValue;
                        if (rowTotal is int totalValue)
                            total = totalValue;
                        if (rowPageSize is int pageSizeValue)
                            pageSize = pageSizeValue;

                        rowIndex++;
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
