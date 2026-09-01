using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using System.Text.Json;

namespace ProjectTraiding.CustomFeatures.Parsing;

public static class ParsingCalendar
{
    private static readonly string[] OffDaysColumns =
        ["tradedate", "is_traded", "trade_session_date", "reason", "updatetime"];

    private static readonly string[] FortsColumns =
    [
        "secid", "asset_code", "shortname", "exec_type", "contract_name",
        "expiration_date", "end_date", "expiration_type", "expiration_time", "weekend_session",
    ];

    public static List<CalendarOffDaysMarketDTO> ParseOffDaysMarket(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "off_days";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rows = CalendarJson.Block(document, rootKey, OffDaysColumns);
        List<CalendarOffDaysMarketDTO> result = new(rows.GetArrayLength());
        int rowIndex = 0;
        foreach (JsonElement row in rows.EnumerateArray())
        {
            CalendarJson.CheckRow(row, OffDaysColumns.Length, rootKey, rowIndex);
            result.Add(new CalendarOffDaysMarketDTO
            {
                TradeDate = CalendarJson.RequiredDate(row, 0, rootKey, "tradedate"),
                IsTraded = CalendarJson.Int(row, 1),
                TradeSessionDate = CalendarJson.Date(row, 2, rootKey, "trade_session_date"),
                Reason = CalendarJson.Str(row, 3),
                UpdateTime = CalendarJson.Stamp(row, 4, rootKey, "updatetime"),
            });
            rowIndex++;
        }
        return result;
    }

    public static List<FuturesExpirationDTO> ParseFuturesSecurities(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "forts";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rows = CalendarJson.Block(document, rootKey, FortsColumns);
        List<FuturesExpirationDTO> result = new(rows.GetArrayLength());
        int rowIndex = 0;
        foreach (JsonElement row in rows.EnumerateArray())
        {
            CalendarJson.CheckRow(row, FortsColumns.Length, rootKey, rowIndex);
            result.Add(new FuturesExpirationDTO
            {
                SecId = CalendarJson.Required(row, 0, rootKey, "secid"),
                AssetCode = CalendarJson.Str(row, 1),
                ExpirationDate = CalendarJson.RequiredDate(row, 5, rootKey, "expiration_date"),
                EndDate = CalendarJson.Date(row, 6, rootKey, "end_date"),
                ExpirationType = CalendarJson.Str(row, 7),
                WeekendSession = CalendarJson.Int16(row, 9, rootKey, "weekend_session"),
            });
            rowIndex++;
        }
        return result;
    }
}
