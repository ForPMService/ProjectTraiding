using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class FutoiParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. FUTOI — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseFutoi_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "futoi": {
                "columns": ["sess_id","seqnum","tradedate","tradetime","ticker","clgroup","pos","pos_long","pos_short","pos_long_num","pos_short_num","systime","trade_session_date"],
                "data": [
                  [1, 100, "2026-05-05", "18:00:00", "Si", "FIZ", 50000, 30000, 20000, 1500, 1200, "2026-05-05 18:00:01", "2026-05-05"],
                  [1, 101, "2026-05-05", "18:00:00", "Si", "YUR", 60000, 35000, 25000, 800, 700, "2026-05-05 18:00:02", "2026-05-05"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<FutoiDTO> result = ParsingAlgUtf8.ParseFutoi(bytes);

            Assert.Equal(2, result.Count);
            Assert.Equal(1, result[0].SessId);
            Assert.Equal(100, result[0].SeqNum);
            Assert.Equal("Si", result[0].Ticker);
            Assert.Equal("FIZ", result[0].ClGroup);
            Assert.Equal(50000L, result[0].Pos);
            Assert.Equal(30000L, result[0].PosLong);
            Assert.Equal(20000L, result[0].PosShort);
            Assert.Equal(1500L, result[0].PosLongNum);
            Assert.Equal(1200L, result[0].PosShortNum);
            Assert.Equal(new DateTime(2026, 5, 5, 18, 0, 1), result[0].SysTime);
            Assert.Equal("2026-05-05", result[0].TradeSessionDate);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. FUTOI — swapped columns → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseFutoi_SwappedColumns_ThrowsSchemaMismatch()
        {
            // sess_id и seqnum переставлены местами
            string json = """
            {
              "futoi": {
                "columns": ["seqnum","sess_id","tradedate","tradetime","ticker","clgroup","pos","pos_long","pos_short","pos_long_num","pos_short_num","systime","trade_session_date"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseFutoi(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 3. FUTOI — short row → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseFutoi_ShortRow_ThrowsSchemaMismatch()
        {
            // Строка содержит только 5 элементов вместо 13
            string json = """
            {
              "futoi": {
                "columns": ["sess_id","seqnum","tradedate","tradetime","ticker","clgroup","pos","pos_long","pos_short","pos_long_num","pos_short_num","systime","trade_session_date"],
                "data": [
                  [1, 100, "2026-05-05", "18:00:00", "Si"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseFutoi(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 4. FUTOI — null values (pos=null, pos_long=null, systime=null)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseFutoi_NullValues_ParsedAsNull()
        {
            string json = """
            {
              "futoi": {
                "columns": ["sess_id","seqnum","tradedate","tradetime","ticker","clgroup","pos","pos_long","pos_short","pos_long_num","pos_short_num","systime","trade_session_date"],
                "data": [
                  [1, 100, "2026-05-05", "18:00:00", "Si", "FIZ", null, null, 20000, 1500, 1200, null, "2026-05-05"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<FutoiDTO> result = ParsingAlgUtf8.ParseFutoi(bytes);

            Assert.Equal(1, result.Count);
            Assert.Null(result[0].Pos);
            Assert.Null(result[0].PosLong);
            Assert.Null(result[0].SysTime);
            Assert.Equal("Si", result[0].Ticker);
            Assert.Equal("FIZ", result[0].ClGroup);
            Assert.Equal(20000L, result[0].PosShort);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. FUTOI — пустой data → пустой list
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseFutoi_EmptyData_ReturnsEmptyList()
        {
            string json = """
            {
              "futoi": {
                "columns": ["sess_id","seqnum","tradedate","tradetime","ticker","clgroup","pos","pos_long","pos_short","pos_long_num","pos_short_num","systime","trade_session_date"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<FutoiDTO> result = ParsingAlgUtf8.ParseFutoi(bytes);

            Assert.Equal(0, result.Count);
        }
    }
}
