using ProjectTraiding.Moex.Contracts.Dto.Iss;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParsingIssCalendarUtf8
    {
        public static List<EngineDailyTableDTO> ParseEngine(ReadOnlySpan<byte> jsonBytes)
        {
            const string rootKey = "dailytable";
            List<EngineDailyTableDTO> result = new List<EngineDailyTableDTO>();
            Utf8JsonReader reader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref reader, rootKey);

            List<string>? columns = null;
            bool foundData = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;
                if (reader.ValueTextEquals("columns"u8))
                {
                    columns = ReadColumns(ref reader, rootKey);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (columns is null)
                        ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Секция 'data' встретилась до 'columns'.");
                    int tradeDateIndex = FindRequiredColumn(columns, rootKey, "tradedate", "trade_date", "date");
                    int startTimeIndex = FindRequiredColumn(columns, rootKey, "start_time", "starttime", "open_time");
                    int stopTimeIndex = FindRequiredColumn(columns, rootKey, "stop_time", "stoptime", "close_time");
                    foundData = true;
                    ReadEngineRows(
                        ref reader, result, columns.Count,
                        tradeDateIndex, startTimeIndex, stopTimeIndex, rootKey);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(columns is not null, foundData, rootKey);
            return result;
        }

        public static List<ListingIntervalDTO> ParseListing(ReadOnlySpan<byte> jsonBytes)
        {
            const string rootKey = "securities";
            List<ListingIntervalDTO> result = new List<ListingIntervalDTO>();
            Utf8JsonReader reader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref reader, rootKey);

            List<string>? columns = null;
            bool foundData = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;
                if (reader.ValueTextEquals("columns"u8))
                {
                    columns = ReadColumns(ref reader, rootKey);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (columns is null)
                        ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Секция 'data' встретилась до 'columns'.");
                    int secIdIndex = FindRequiredColumn(columns, rootKey, "secid");
                    int boardIdIndex = FindRequiredColumn(columns, rootKey, "boardid");
                    int historyFromIndex = FindRequiredColumn(columns, rootKey, "history_from");
                    int historyTillIndex = FindRequiredColumn(columns, rootKey, "history_till");
                    foundData = true;
                    ReadListingRows(
                        ref reader, result, columns.Count,
                        secIdIndex, boardIdIndex, historyFromIndex, historyTillIndex, rootKey);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(columns is not null, foundData, rootKey);
            return result;
        }

        public static List<SplitDTO> ParseSplits(ReadOnlySpan<byte> jsonBytes)
        {
            const string rootKey = "splits";
            List<SplitDTO> result = new List<SplitDTO>();
            Utf8JsonReader reader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref reader, rootKey);

            List<string>? columns = null;
            bool foundData = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;
                if (reader.ValueTextEquals("columns"u8))
                {
                    columns = ReadColumns(ref reader, rootKey);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (columns is null)
                        ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Секция 'data' встретилась до 'columns'.");
                    int tradeDateIndex = FindRequiredColumn(columns, rootKey, "tradedate", "trade_date");
                    int secIdIndex = FindRequiredColumn(columns, rootKey, "secid");
                    int beforeIndex = FindRequiredColumn(columns, rootKey, "before", "before_qty");
                    int afterIndex = FindRequiredColumn(columns, rootKey, "after", "after_qty");
                    foundData = true;
                    ReadSplitRows(
                        ref reader, result, columns.Count,
                        tradeDateIndex, secIdIndex, beforeIndex, afterIndex, rootKey);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(columns is not null, foundData, rootKey);
            return result;
        }

        private static List<string> ReadColumns(ref Utf8JsonReader reader, string rootKey)
        {
            List<string> columns = new List<string>();
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "columns", rootKey);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                    ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Имя колонки должно быть строкой.");
                string? column = reader.GetString();
                if (column is null)
                    ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Имя колонки не может быть null.");
                columns.Add(column);
            }
            return columns;
        }

        private static int FindRequiredColumn(
            List<string> columns,
            string rootKey,
            params string[] acceptedNames)
        {
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                for (int nameIndex = 0; nameIndex < acceptedNames.Length; nameIndex++)
                {
                    if (string.Equals(
                        columns[columnIndex], acceptedNames[nameIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        return columnIndex;
                    }
                }
            }

            ParseHelpersUtf8.SchemaMismatch(
                $"[{rootKey}] Не найдена обязательная колонка '{acceptedNames[0]}'.");
            return -1;
        }

        private static void ReadEngineRows(
            ref Utf8JsonReader reader,
            List<EngineDailyTableDTO> result,
            int totalColumns,
            int tradeDateIndex,
            int startTimeIndex,
            int stopTimeIndex,
            string rootKey)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", rootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? tradeDate = null;
                string? startTime = null;
                string? stopTime = null;
                for (int position = 0; position < totalColumns; position++)
                {
                    ReadRowValue(ref reader, totalColumns, rowIndex, position, rootKey);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    if (position == tradeDateIndex)
                        tradeDate = ReadString(ref reader, rowIndex, position, rootKey);
                    else if (position == startTimeIndex)
                        startTime = ReadString(ref reader, rowIndex, position, rootKey);
                    else if (position == stopTimeIndex)
                        stopTime = ReadString(ref reader, rowIndex, position, rootKey);
                    else
                        reader.Skip();
                }
                ExpectRowEnd(ref reader, totalColumns, rowIndex, rootKey);
                result.Add(new EngineDailyTableDTO
                {
                    TradeDate = tradeDate,
                    StartTime = startTime,
                    StopTime = stopTime,
                });
                rowIndex++;
            }
        }

        private static void ReadListingRows(
            ref Utf8JsonReader reader,
            List<ListingIntervalDTO> result,
            int totalColumns,
            int secIdIndex,
            int boardIdIndex,
            int historyFromIndex,
            int historyTillIndex,
            string rootKey)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", rootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? secId = null;
                string? boardId = null;
                string? historyFrom = null;
                string? historyTill = null;
                for (int position = 0; position < totalColumns; position++)
                {
                    ReadRowValue(ref reader, totalColumns, rowIndex, position, rootKey);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    if (position == secIdIndex)
                        secId = ReadString(ref reader, rowIndex, position, rootKey);
                    else if (position == boardIdIndex)
                        boardId = ReadString(ref reader, rowIndex, position, rootKey);
                    else if (position == historyFromIndex)
                        historyFrom = ReadString(ref reader, rowIndex, position, rootKey);
                    else if (position == historyTillIndex)
                        historyTill = ReadString(ref reader, rowIndex, position, rootKey);
                    else
                        reader.Skip();
                }
                ExpectRowEnd(ref reader, totalColumns, rowIndex, rootKey);
                result.Add(new ListingIntervalDTO
                {
                    SecId = secId,
                    BoardId = boardId,
                    HistoryFrom = historyFrom,
                    HistoryTill = historyTill,
                });
                rowIndex++;
            }
        }

        private static void ReadSplitRows(
            ref Utf8JsonReader reader,
            List<SplitDTO> result,
            int totalColumns,
            int tradeDateIndex,
            int secIdIndex,
            int beforeIndex,
            int afterIndex,
            string rootKey)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", rootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? tradeDate = null;
                string? secId = null;
                int? beforeQty = null;
                int? afterQty = null;
                for (int position = 0; position < totalColumns; position++)
                {
                    ReadRowValue(ref reader, totalColumns, rowIndex, position, rootKey);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    if (position == tradeDateIndex)
                        tradeDate = ReadString(ref reader, rowIndex, position, rootKey);
                    else if (position == secIdIndex)
                        secId = ReadString(ref reader, rowIndex, position, rootKey);
                    else if (position == beforeIndex)
                        beforeQty = ReadInt(ref reader, rowIndex, position, rootKey);
                    else if (position == afterIndex)
                        afterQty = ReadInt(ref reader, rowIndex, position, rootKey);
                    else
                        reader.Skip();
                }
                ExpectRowEnd(ref reader, totalColumns, rowIndex, rootKey);
                result.Add(new SplitDTO
                {
                    TradeDate = tradeDate,
                    SecId = secId,
                    BeforeQty = beforeQty,
                    AfterQty = afterQty,
                });
                rowIndex++;
            }
        }

        private static void ReadRowValue(
            ref Utf8JsonReader reader,
            int totalColumns,
            int rowIndex,
            int position,
            string rootKey)
        {
            if (!reader.Read())
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{rootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {position}.");
            if (reader.TokenType == JsonTokenType.EndArray)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{rootKey}] Короткая строка данных: ожидалось {totalColumns} колонок, " +
                    $"получено {position} (строка {rowIndex}).");
        }

        private static void ExpectRowEnd(
            ref Utf8JsonReader reader,
            int totalColumns,
            int rowIndex,
            string rootKey)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{rootKey}] Ожидался EndArray после {totalColumns} колонок (строка {rowIndex}).");
        }

        private static string? ReadString(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            return ParseHelpersUtf8.ReadString(ref reader, rowIndex, columnIndex, rootKey);
        }

        private static int ReadInt(
            ref Utf8JsonReader reader,
            int rowIndex,
            int columnIndex,
            string rootKey)
        {
            return ParseHelpersUtf8.ReadInt(ref reader, rowIndex, columnIndex, rootKey);
        }
    }
}
