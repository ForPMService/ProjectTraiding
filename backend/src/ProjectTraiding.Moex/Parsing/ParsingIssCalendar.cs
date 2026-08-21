using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParsingIssCalendar
    {
        // Ответ /engines/{engine}.json, раздел dailytable — таблица дней-исключений.
        private static readonly string[] EngineColumns =
            { "date", "is_work_day", "start_time", "stop_time" };

        // Ответ /history/engines/{engine}/markets/{market}/listing.json.
        // Раздел называется securities, а не listing.
        private static readonly string[] ListingColumns =
        {
            "SECID", "SHORTNAME", "NAME", "BOARDID", "decimals", "history_from", "history_till",
        };

        // Ответ /statistics/engines/stock/splits.json.
        private static readonly string[] SplitsColumns =
            { "tradedate", "secid", "before", "after" };

        public static List<EngineDailyTableDTO> ParseEngine(ReadOnlyMemory<byte> json)
        {
            const string rootKey = "dailytable";
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement rows = IssJson.Block(document, rootKey, EngineColumns);
            List<EngineDailyTableDTO> result = new List<EngineDailyTableDTO>(rows.GetArrayLength());
            int rowIndex = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                IssJson.CheckRow(row, EngineColumns.Length, rootKey, rowIndex);
                result.Add(new EngineDailyTableDTO
                {
                    TradeDate = IssJson.RequiredDate(row, 0, rootKey, "date"),
                    IsWorkDay = IssJson.Int(row, 1),
                    StartTime = IssJson.Time(row, 2, rootKey, "start_time"),
                    StopTime = IssJson.Time(row, 3, rootKey, "stop_time"),
                });
                rowIndex++;
            }
            return result;
        }

        public static List<ListingIntervalDTO> ParseListing(ReadOnlyMemory<byte> json)
        {
            const string rootKey = "securities";
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement rows = IssJson.Block(document, rootKey, ListingColumns);
            List<ListingIntervalDTO> result = new List<ListingIntervalDTO>(rows.GetArrayLength());
            int rowIndex = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                IssJson.CheckRow(row, ListingColumns.Length, rootKey, rowIndex);
                result.Add(new ListingIntervalDTO
                {
                    SecId = IssJson.Required(row, 0, rootKey, "SECID"),
                    BoardId = IssJson.Required(row, 3, rootKey, "BOARDID"),
                    HistoryFrom = IssJson.Date(row, 5, rootKey, "history_from"),
                    HistoryTill = IssJson.Date(row, 6, rootKey, "history_till"),
                });
                rowIndex++;
            }
            return result;
        }

        public static List<SplitWriteDTO> ParseSplits(ReadOnlyMemory<byte> json)
        {
            const string rootKey = "splits";
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement rows = IssJson.Block(document, rootKey, SplitsColumns);
            List<SplitWriteDTO> result = new List<SplitWriteDTO>(rows.GetArrayLength());
            int rowIndex = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                IssJson.CheckRow(row, SplitsColumns.Length, rootKey, rowIndex);
                int? before = IssJson.Int(row, 2);
                int? after = IssJson.Int(row, 3);
                if (before is null || after is null)
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{rootKey}] Строка {rowIndex}: пустое отношение дробления.");
                result.Add(new SplitWriteDTO
                {
                    TradeDate = IssJson.RequiredDate(row, 0, rootKey, "tradedate"),
                    SecId = IssJson.Required(row, 1, rootKey, "secid"),
                    BeforeQty = before!.Value,
                    AfterQty = after!.Value,
                });
                rowIndex++;
            }
            return result;
        }
    }
}
