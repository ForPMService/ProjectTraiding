
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Buffers.Text;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParseHelpersUtf8
    {
        private static readonly ColumnAndNumbersForParsing.ExpectedSchema CursorSchema = new(
            TotalColumns: 3,
            RootKey: "data.cursor",
            Columns: new ColumnAndNumbersForParsing.ExpectedColumn[]
            {
                new(0, "INDEX"u8.ToArray()),
                new(1, "TOTAL"u8.ToArray()),
                new(2, "PAGESIZE"u8.ToArray()),
            });

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
        internal static void SkipToRootObject(
            ref Utf8JsonReader reader,
            string rootKey)
        {
            // Пройти до StartObject верхнего уровня
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                    break;
            }

            // Внутри корневого объекта JSON — ищем свойство rootKey
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    SchemaMismatch(
                        $"MOEX ответ не содержит корневой ключ '{rootKey}'.");
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                // Сравнение с именем в виде строки перекодирует короткое имя во временный
                // буфер на стеке и кучу не задействует. Прежний приём создавал массив байтов
                // на каждый разбор страницы. Раскодирование экранированных имён свойств
                // обе перегрузки выполняют одинаково.
                if (reader.ValueTextEquals(rootKey))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    {
                        SchemaMismatch(
                            $"[{rootKey}] Ожидался объект (StartObject), " +
                            $"получено {reader.TokenType}.");
                    }

                    return;
                }

                reader.Skip();
            }

            SchemaMismatch(
                $"MOEX ответ не содержит корневой ключ '{rootKey}'.");
        }

        // ═══════════════════════════════════════════════════════════
        // Валидация columns[]
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Проверка массива columns[] — порядок и количество.
        /// 
        /// Для схем без пропусков — проверяет все колонки подряд.
        /// Для схем с пропусками (ISS Securities) — проверяет только указанные позиции.
        /// 
        /// Бросает MoexSchemaMismatchException при несовпадении имени колонки или
        /// при неправильном количестве колонок (Lock §10).
        /// </summary>
        internal static void ValidateColumnsUtf8(
            ref Utf8JsonReader reader,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ReadAndExpect(ref reader, JsonTokenType.StartArray, "columns", schema.RootKey);

            int position = 0;
            int expectedIdx = 0;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (expectedIdx < schema.Columns.Length
                    && position == schema.Columns[expectedIdx].SourceIndex)
                {
                    if (!reader.ValueTextEquals(schema.Columns[expectedIdx].Name))
                    {
                        string actual = reader.GetString() ?? "<null>";
                        string expected = System.Text.Encoding.UTF8.GetString(
                            schema.Columns[expectedIdx].Name);

                        throw new MoexSchemaMismatchException(
                            $"[{schema.RootKey}] Колонка не совпала на позиции {position}: " +
                            $"ожидалось '{expected}', получено '{actual}'.");
                    }
                    expectedIdx++;
                }
                position++;
            }

            if (position != schema.TotalColumns)
                SchemaMismatch(
                    $"[{schema.RootKey}] Количество колонок не совпадает: " +
                    $"ожидалось {schema.TotalColumns}, получено {position}.");

            if (expectedIdx != schema.Columns.Length)
                SchemaMismatch(
                    $"[{schema.RootKey}] Не все ожидаемые колонки найдены: " +
                    $"ожидалось {schema.Columns.Length}, проверено {expectedIdx}.");
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
        /// Прочитать string из текущего токена.
        /// </summary>
        internal static string? ReadString(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException(
                    $"[{rootKey}] Ожидался String в строке {rowIndex}, колонка {columnIndex}, " +
                    $"получено {reader.TokenType}.");

            return reader.GetString();
        }

        // ═══════════════════════════════════════════════════════════
        // DateTime без аллокаций (A5)
        // ═══════════════════════════════════════════════════════════

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

            // ValueSpan доступен когда JSON не фрагментирован (ReadOnlySpan<byte> вход).
            // Для PipeReader/Stream в будущем (D1) может понадобиться fallback на GetString().
            ReadOnlySpan<byte> span = reader.HasValueSequence
                ? reader.ValueSequence.ToArray()
                : reader.ValueSpan;

            // "yyyy-MM-dd HH:mm:ss" = 19 байт
            if (span.Length != 19)
                return null;

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
            // Клон схемы ради подстановки корневого ключа больше не создаётся: ключ
            // постоянен и объявлен в самой схеме, единственным источником правды.
            ColumnAndNumbersForParsing.ExpectedSchema schema = CursorSchema;

            ReadAndExpect(ref reader, JsonTokenType.StartObject, schema.RootKey, schema.RootKey);

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
                    ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        SchemaMismatch(
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. " +
                            $"Порядок columns → data обязателен.");

                    foundData = true;
                    ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

                    int rowIndex = 0;
                    while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                    {
                        {
                            int expectedIdx = 0;
                            // 0=INDEX 1=TOTAL 2=PAGESIZE
                            for (int pos = 0; pos < schema.TotalColumns; pos++)
                            {
                                if (!reader.Read())
                                    SchemaMismatch(
                                        $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {pos}.");

                                if (reader.TokenType == JsonTokenType.EndArray)
                                    SchemaMismatch(
                                        $"[{schema.RootKey}] Короткая строка данных: " +
                                        $"ожидалось {schema.TotalColumns} колонок, получено {pos} " +
                                        $"(строка {rowIndex}).");

                                if (expectedIdx < schema.Columns.Length
                                    && pos == schema.Columns[expectedIdx].SourceIndex)
                                {
                                    if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        switch (expectedIdx)
                                        {
                                            case 0: index = ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                            case 1: total = ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                            case 2: pageSize = ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                        }
                                    }

                                    expectedIdx++;
                                }
                            }

                            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                                SchemaMismatch(
                                    $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                        }

                        rowIndex++;
                    }
                    // reader стоит на EndArray внешнего data[]
                }
                else
                {
                    reader.Skip();
                }
            }

            ValidateStructure(foundColumns, foundData, schema.RootKey);

            return new PaginationCursorDTO
            {
                Index    = index,
                Total    = total,
                PageSize = pageSize,
            };
        }

    }
}
