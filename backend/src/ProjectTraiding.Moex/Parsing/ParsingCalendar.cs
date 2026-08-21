using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParsingCalendar
    {
        private static readonly string[] OffDaysColumns =
            { "tradedate", "is_traded", "trade_session_date", "reason", "updatetime" };

        private static readonly string[] FortsColumns =
        {
            "secid", "asset_code", "shortname", "exec_type", "contract_name",
            "expiration_date", "end_date", "expiration_type", "expiration_time", "weekend_session",
        };

        public static List<CalendarOffDaysMarketDTO> ParseOffDaysMarket(ReadOnlyMemory<byte> json)
        {
            const string rootKey = "off_days";
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement rows = IssJson.Block(document, rootKey, OffDaysColumns);
            List<CalendarOffDaysMarketDTO> result =
                new List<CalendarOffDaysMarketDTO>(rows.GetArrayLength());
            int rowIndex = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                IssJson.CheckRow(row, OffDaysColumns.Length, rootKey, rowIndex);
                result.Add(new CalendarOffDaysMarketDTO
                {
                    TradeDate = IssJson.RequiredDate(row, 0, rootKey, "tradedate"),
                    IsTraded = IssJson.Int(row, 1),
                    TradeSessionDate = IssJson.Date(row, 2, rootKey, "trade_session_date"),
                    Reason = IssJson.Str(row, 3),
                    UpdateTime = IssJson.Stamp(row, 4, rootKey, "updatetime"),
                });
                rowIndex++;
            }
            return result;
        }

        public static List<FuturesExpirationDTO> ParseFuturesSecurities(ReadOnlyMemory<byte> json)
        {
            const string rootKey = "forts";
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement rows = IssJson.Block(document, rootKey, FortsColumns);
            List<FuturesExpirationDTO> result =
                new List<FuturesExpirationDTO>(rows.GetArrayLength());
            int rowIndex = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                IssJson.CheckRow(row, FortsColumns.Length, rootKey, rowIndex);
                result.Add(new FuturesExpirationDTO
                {
                    SecId = IssJson.Required(row, 0, rootKey, "secid"),
                    AssetCode = IssJson.Str(row, 1),
                    ExpirationDate = IssJson.RequiredDate(row, 5, rootKey, "expiration_date"),
                    EndDate = IssJson.Date(row, 6, rootKey, "end_date"),
                    ExpirationType = IssJson.Str(row, 7),
                    WeekendSession = IssJson.Int16(row, 9, rootKey, "weekend_session"),
                });
                rowIndex++;
            }
            return result;
        }
    }
}
