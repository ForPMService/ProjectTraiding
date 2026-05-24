using History_DataMoex.Contracts.Dto.Calendar;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class CalendarMultiTableParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. StockSession happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseStockSession_HappyPath_ReturnsBothTables()
        {
            string json = """
            {
              "session_schedule": {
                "columns": ["tradedate", "tradingsession", "boardid", "secid", "type", "time_from", "time_till", "updatetime"],
                "data": [
                  ["2026-05-07", -999, "TQBR", "", "oa_booking", "06:50:00", "09:59:00", "2026-05-06 18:00:00"],
                  ["2026-05-07", -999, "TQBR", "", "system", "10:00:00", "18:39:59", "2026-05-06 18:00:00"]
                ]
              },
              "session_schedule.types": {
                "columns": ["type", "title"],
                "data": [
                  ["oa_booking", "Аукцион открытия"],
                  ["system", "Системный режим"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var (sessions, types) = ParsingCalendarUtf8.ParseStockSession(bytes);

            Assert.Equal(2, sessions.Count);
            Assert.Equal(2, types.Count);

            Assert.Equal("2026-05-07", sessions[0].TradeDate);
            Assert.Equal(-999, sessions[0].TradingSession);
            Assert.Equal("TQBR", sessions[0].BoardId);
            Assert.Equal("", sessions[0].SecId);
            Assert.Equal("oa_booking", sessions[0].Type);
            Assert.Equal("06:50:00", sessions[0].TimeFrom);
            Assert.Equal("09:59:00", sessions[0].TimeTill);
            Assert.Equal(new DateTime(2026, 5, 6, 18, 0, 0), sessions[0].UpdateTime);

            Assert.Equal("system", sessions[1].Type);

            Assert.Equal("oa_booking", types[0].Type);
            Assert.Equal("Аукцион открытия", types[0].Title);
            Assert.Equal("system", types[1].Type);
            Assert.Equal("Системный режим", types[1].Title);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. FuturesSecurities happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseFuturesSecurities_HappyPath_ReturnsBothTables()
        {
            string json = """
            {
              "forts": {
                "columns": ["secid", "asset_code", "shortname", "exec_type", "contract_name", "expiration_date", "end_date", "expiration_type", "expiration_time", "weekend_session"],
                "data": [
                  ["SiM6", "Si", "SiM6", "S", "Фьючерс на USD/RUB", "2026-06-18", "2026-06-18", "D", "18:30:00", 0],
                  ["BRN6", "BR", "BRN6", "S", "Фьючерс на нефть Brent", "2026-06-01", "2026-06-01", "D", "18:45:00", 1]
                ]
              },
              "options": {
                "columns": ["asset_type_name", "asset_code", "series_name", "series_type", "exec_type", "margin_style", "contract_name", "expiration_date", "expiration_type", "expiration_time", "weekend_session"],
                "data": [
                  ["Акции", "ALRS", "ALRSP190630XE", "Q", "E", "P", "Опцион на акции АЛРОСА", "2030-06-19", "D", "18:30:00", 0],
                  ["Валюта", "Si", "SiM6XA", "Q", "A", "P", "Опцион на фьючерс USD/RUB", "2026-06-18", "D", "18:30:00", 0]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var (forts, options) = ParsingCalendarUtf8.ParseFuturesSecurities(bytes);

            Assert.Equal(2, forts.Count);
            Assert.Equal(2, options.Count);

            Assert.Equal("SiM6", forts[0].SecId);
            Assert.Equal("Si", forts[0].AssetCode);
            Assert.Equal("SiM6", forts[0].ShortName);
            Assert.Equal("S", forts[0].ExecType);
            Assert.Equal("Фьючерс на USD/RUB", forts[0].ContractName);
            Assert.Equal("2026-06-18", forts[0].ExpirationDate);
            Assert.Equal("2026-06-18", forts[0].EndDate);
            Assert.Equal("D", forts[0].ExpirationType);
            Assert.Equal("18:30:00", forts[0].ExpirationTime);
            Assert.Equal(0, forts[0].WeekendSession);

            Assert.Equal("BRN6", forts[1].SecId);
            Assert.Equal(1, forts[1].WeekendSession);

            Assert.Equal("ALRSP190630XE", options[0].SeriesName);
            Assert.Equal("Акции", options[0].AssetTypeName);
            Assert.Equal("ALRS", options[0].AssetCode);
            Assert.Equal("P", options[0].MarginStyle);

            Assert.Equal("SiM6XA", options[1].SeriesName);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. StockSession — missing table (session_schedule absent)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseStockSession_MissingSessionSchedule_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "session_schedule.types": {
                "columns": ["type", "title"],
                "data": [
                  ["oa_booking", "Аукцион открытия"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingCalendarUtf8.ParseStockSession(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 4. FuturesSecurities — swapped columns in forts
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseFuturesSecurities_SwappedFortsColumns_ThrowsSchemaMismatch()
        {
            // Первые две колонки переставлены: asset_code идёт до secid
            string json = """
            {
              "forts": {
                "columns": ["asset_code", "secid", "shortname", "exec_type", "contract_name", "expiration_date", "end_date", "expiration_type", "expiration_time", "weekend_session"],
                "data": []
              },
              "options": {
                "columns": ["asset_type_name", "asset_code", "series_name", "series_type", "exec_type", "margin_style", "contract_name", "expiration_date", "expiration_type", "expiration_time", "weekend_session"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingCalendarUtf8.ParseFuturesSecurities(bytes));
        }
    }
}
