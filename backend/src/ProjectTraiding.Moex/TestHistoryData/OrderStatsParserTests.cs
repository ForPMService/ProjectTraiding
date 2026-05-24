using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class OrderStatsParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. OrderStats Stock — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderStatsStock_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","put_orders_b","put_orders_s","put_val_b","put_val_s","put_vol_b","put_vol_s","put_vwap_b","put_vwap_s","put_vol","put_val","put_orders","cancel_orders_b","cancel_orders_s","cancel_val_b","cancel_val_s","cancel_vol_b","cancel_vol_s","cancel_vwap_b","cancel_vwap_s","cancel_vol","cancel_val","cancel_orders","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SBER",100,90,3000000.0,2700000.0,10000,9000,300.5,300.3,19000,5700000.0,190,50,45,1500000.0,1350000.0,5000,4500,300.4,300.2,9500,2850000.0,95,"2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SBER",110,95,3300000.0,2850000.0,11000,9500,300.6,300.4,20500,6150000.0,205,55,50,1650000.0,1500000.0,5500,5000,300.5,300.3,10500,3150000.0,105,"2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesOrderStats5mDTO> result = ParsingAlgUtf8.ParseOrderStatsStock(bytes);

            Assert.Equal(2, result.Count);

            Assert.Equal("2026-05-05", result[0].TradeDate);
            Assert.Equal("SBER", result[0].SecId);
            Assert.Equal(100, result[0].PutOrdersB);
            Assert.Equal(3000000.0, result[0].PutValB);
            Assert.Equal(4500L, result[0].CancelVolS);
            Assert.Equal(95L, result[0].CancelOrders);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. OrderStats Stock — swapped columns → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderStatsStock_SwappedColumns_ThrowsSchemaMismatch()
        {
            // tradetime и tradedate поменяны местами
            string json = """
            {
              "data": {
                "columns": ["tradetime","tradedate","secid","put_orders_b","put_orders_s","put_val_b","put_val_s","put_vol_b","put_vol_s","put_vwap_b","put_vwap_s","put_vol","put_val","put_orders","cancel_orders_b","cancel_orders_s","cancel_val_b","cancel_val_s","cancel_vol_b","cancel_vol_s","cancel_vwap_b","cancel_vwap_s","cancel_vol","cancel_val","cancel_orders","SYSTIME"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseOrderStatsStock(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 3. OrderStats Stock — short row → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderStatsStock_ShortRow_ThrowsSchemaMismatch()
        {
            // Строка содержит только 10 элементов вместо 26
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","put_orders_b","put_orders_s","put_val_b","put_val_s","put_vol_b","put_vol_s","put_vwap_b","put_vwap_s","put_vol","put_val","put_orders","cancel_orders_b","cancel_orders_s","cancel_val_b","cancel_val_s","cancel_vol_b","cancel_vol_s","cancel_vwap_b","cancel_vwap_s","cancel_vol","cancel_val","cancel_orders","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SBER",100,90,3000000.0,2700000.0,10000,9000,300.5]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseOrderStatsStock(bytes));
        }
    }
}
