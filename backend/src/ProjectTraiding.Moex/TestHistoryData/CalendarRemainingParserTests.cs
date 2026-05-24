using History_DataMoex.Contracts.Dto.Calendar;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class CalendarRemainingParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. OffDaysAll happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOffDaysAll_HappyPath_ReturnsTwoRows()
        {
            string json = """
            {
              "off_days": {
                "columns": ["tradedate", "currency_workday", "currency_trade_session_date", "currency_reason", "futures_workday", "futures_trade_session_date", "futures_reason", "stock_workday", "stock_trade_session_date", "stock_reason"],
                "data": [
                  ["2026-01-01", 0, null, "H", 0, null, "H", 0, null, "H"],
                  ["2026-01-07", 1, "2026-01-07", "W", 1, "2026-01-07", "W", 0, null, "H"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            List<CalendarOffDaysAllDTO> result = ParsingCalendarUtf8.ParseOffDaysAll(bytes);

            Assert.Equal(2, result.Count);

            Assert.Equal("2026-01-01", result[0].TradeDate);
            Assert.Equal(0L, result[0].CurrencyWorkday);
            Assert.Null(result[0].CurrencyTradeSessionDate);
            Assert.Equal("H", result[0].CurrencyReason);
            Assert.Equal(0L, result[0].FuturesWorkday);
            Assert.Null(result[0].FuturesTradeSessionDate);
            Assert.Equal("H", result[0].FuturesReason);
            Assert.Equal(0L, result[0].StockWorkday);
            Assert.Null(result[0].StockTradeSessionDate);
            Assert.Equal("H", result[0].StockReason);

            Assert.Equal("2026-01-07", result[1].TradeDate);
            Assert.Equal(1L, result[1].CurrencyWorkday);
            Assert.Equal("W", result[1].CurrencyReason);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. OffDaysMarket happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOffDaysMarket_HappyPath_ReturnsTwoRows()
        {
            string json = """
            {
              "off_days": {
                "columns": ["tradedate", "is_traded", "trade_session_date", "reason", "updatetime"],
                "data": [
                  ["2026-01-18", 1, "2026-01-19", "W", "2026-01-17 20:00:00"],
                  ["2026-01-01", 0, null, "H", "2025-12-30 18:00:00"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            List<CalendarOffDaysMarketDTO> result = ParsingCalendarUtf8.ParseOffDaysMarket(bytes);

            Assert.Equal(2, result.Count);

            Assert.Equal("2026-01-18", result[0].TradeDate);
            Assert.Equal(1, result[0].IsTraded);
            Assert.Equal("2026-01-19", result[0].TradeSessionDate);
            Assert.Equal("W", result[0].Reason);
            Assert.Equal(new DateTime(2026, 1, 17, 20, 0, 0), result[0].UpdateTime);

            Assert.Equal(0, result[1].IsTraded);
            Assert.Null(result[1].TradeSessionDate);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. SuspendedWithReasons happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseSuspendedWithReasons_HappyPath_ReturnsAllThreeParts()
        {
            string json = """
            {
              "suspended": {
                "columns": ["secid", "reason_id", "date_from", "date_till", "boardid", "settle_codes", "changedate", "updatetime"],
                "data": [
                  ["AGNC-RM", "5002", "2026-01-05", null, "MPTR", "Y2-14", "2025-12-30", "2025-12-30 10:00:00"],
                  ["VTBR-RM", "5001", "2025-11-01", "2026-01-31", "TQBR", "Y1", "2025-10-15", "2025-10-15 09:00:00"]
                ]
              },
              "suspended.reasons": {
                "columns": ["id", "title"],
                "data": [
                  [1, "Торги не проводятся в дату погашения облигаций"],
                  [5002, "Административная приостановка"]
                ]
              },
              "suspended.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [
                  [0, 160000, 100]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var (suspended, reasons, cursor) = ParsingCalendarUtf8.ParseSuspendedWithReasons(bytes);

            Assert.Equal(2, suspended.Count);
            Assert.Equal(2, reasons.Count);
            Assert.NotNull(cursor.Total);

            Assert.Equal("AGNC-RM", suspended[0].SecId);
            Assert.Equal("5002", suspended[0].ReasonId);   // string, не int
            Assert.Equal("2026-01-05", suspended[0].DateFrom);
            Assert.Null(suspended[0].DateTill);
            Assert.Equal("MPTR", suspended[0].BoardId);
            Assert.Equal("Y2-14", suspended[0].SettleCodes);
            Assert.Equal("2025-12-30", suspended[0].ChangeDate);
            Assert.Equal(new DateTime(2025, 12, 30, 10, 0, 0), suspended[0].UpdateTime);

            Assert.Equal(1, reasons[0].Id);   // int
            Assert.Equal("Торги не проводятся в дату погашения облигаций", reasons[0].Title);
            Assert.Equal(5002, reasons[1].Id);

            Assert.Equal(0, cursor.Index);
            Assert.Equal(160000, cursor.Total);
            Assert.Equal(100, cursor.PageSize);
        }

        // ═══════════════════════════════════════════════════════════
        // 4. SecurityChangesWithAttributes happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseSecurityChangesWithAttributes_HappyPath_ReturnsAllThreeParts()
        {
            string json = """
            {
              "securities": {
                "columns": ["updatetime", "action", "secid", "attribute_name", "before_value", "after_value"],
                "data": [
                  ["2026-05-07 00:21:04", "updated", "RU000A0JXR84", "COUPONDATE", "2026-05-07", "2026-11-05"],
                  ["2026-05-06 18:00:00", "inserted", "RU000A0ZZZZ1", "MATDATE", null, "2027-01-01"]
                ]
              },
              "securities.attributes": {
                "columns": ["name", "type", "title"],
                "data": [
                  ["COUPONDATE", "D", "Дата выплаты купона"],
                  ["MATDATE", "D", "Дата погашения"]
                ]
              },
              "securities.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [
                  [0, 5000, 100]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var (changes, attributes, cursor) = ParsingCalendarUtf8.ParseSecurityChangesWithAttributes(bytes);

            Assert.Equal(2, changes.Count);
            Assert.Equal(2, attributes.Count);
            Assert.NotNull(cursor.Total);

            Assert.Equal(new DateTime(2026, 5, 7, 0, 21, 4), changes[0].UpdateTime);
            Assert.Equal("updated", changes[0].Action);
            Assert.Equal("RU000A0JXR84", changes[0].SecId);
            Assert.Equal("COUPONDATE", changes[0].AttributeName);
            Assert.Equal("2026-05-07", changes[0].BeforeValue);
            Assert.Equal("2026-11-05", changes[0].AfterValue);

            Assert.Null(changes[1].BeforeValue);

            Assert.Equal("COUPONDATE", attributes[0].Name);
            Assert.Equal("D", attributes[0].Type);
            Assert.Equal("Дата выплаты купона", attributes[0].Title);

            Assert.Equal("MATDATE", attributes[1].Name);

            Assert.Equal(0, cursor.Index);
            Assert.Equal(5000, cursor.Total);
            Assert.Equal(100, cursor.PageSize);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. SuspendedWithReasons — missing suspended table
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseSuspendedWithReasons_MissingSuspendedTable_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "suspended.reasons": {
                "columns": ["id", "title"],
                "data": [
                  [1, "Торги не проводятся в дату погашения облигаций"]
                ]
              },
              "suspended.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [
                  [0, 100, 100]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingCalendarUtf8.ParseSuspendedWithReasons(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 6. OffDaysAll — swapped columns
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOffDaysAll_SwappedColumns_ThrowsSchemaMismatch()
        {
            // currency_workday и tradedate переставлены местами
            string json = """
            {
              "off_days": {
                "columns": ["currency_workday", "tradedate", "currency_trade_session_date", "currency_reason", "futures_workday", "futures_trade_session_date", "futures_reason", "stock_workday", "stock_trade_session_date", "stock_reason"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingCalendarUtf8.ParseOffDaysAll(bytes));
        }
    }
}
