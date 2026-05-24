using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class TradeStatsParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. TradeStats Stock — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradeStatsStock_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","pr_open","pr_high","pr_low","pr_close","pr_std","vol","val","trades","pr_vwap","pr_change","trades_b","trades_s","val_b","val_s","vol_b","vol_s","disb","pr_vwap_b","pr_vwap_s","SYSTIME","sec_pr_open","sec_pr_high","sec_pr_low","sec_pr_close"],
                "data": [
                  ["2026-05-05","10:05:00","SBER",300.5,301.0,299.0,300.8,0.5,1000,300500.0,50,300.6,0.1,25,25,150250.0,150250.0,500,500,0.0,300.5,300.7,"2026-05-05 10:05:01",5,10,15,20],
                  ["2026-05-05","10:10:00","SBER",300.8,302.0,300.0,301.5,0.6,2000,601000.0,80,300.9,0.7,40,40,300500.0,300500.0,1000,1000,0.1,300.8,301.0,"2026-05-05 10:10:01",6,11,16,21]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesTradeStats5mDTO> result = ParsingAlgUtf8.ParseTradeStatsStock(bytes);

            Assert.Equal(2, result.Count);

            // Проверяем ключевые поля первой строки
            Assert.Equal("2026-05-05", result[0].TradeDate);
            Assert.Equal("SBER", result[0].SecId);
            Assert.Equal(300.5, result[0].PrOpen);
            Assert.Equal(1000, result[0].Vol);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
            Assert.Equal(5, result[0].SecPrOpen);
            Assert.Equal(20, result[0].SecPrClose);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. TradeStats Futures — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradeStatsFutures_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","pr_open","pr_high","pr_low","pr_close","pr_std","vol","val","trades","pr_vwap","pr_change","trades_b","trades_s","val_b","val_s","vol_b","vol_s","disb","pr_vwap_b","pr_vwap_s","im","oi_open","oi_high","oi_low","oi_close","sec_pr_open","sec_pr_high","sec_pr_low","sec_pr_close","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si",85000.0,85100.0,84900.0,85050.0,50.0,5000,425000000,200,85025.0,50.0,100,100,212500000.0,212500000.0,2500,2500,0.0,85020.0,85030.0,15000.0,100000,100500,99500,100200,5,10,15,20,"2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SiM6","Si",85050.0,85200.0,84950.0,85150.0,55.0,6000,510000000,220,85100.0,100.0,110,110,255000000.0,255000000.0,3000,3000,0.05,85090.0,85110.0,15100.0,100200,100800,99800,100600,6,11,16,21,"2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesFuturesTradeStats5mDTO> result = ParsingAlgUtf8.ParseTradeStatsFutures(bytes);

            Assert.Equal(2, result.Count);

            // Проверяем ключевые поля первой строки
            Assert.Equal("Si", result[0].AssetCode);
            Assert.Equal(15000.0, result[0].Im);
            Assert.Equal(100000L, result[0].OiOpen);
            Assert.Equal(100200L, result[0].OiClose);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
            Assert.Equal(5000L, result[0].Vol);
            Assert.Equal(20, result[0].SecPrClose);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. TradeStats Stock — swapped columns → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradeStatsStock_SwappedColumns_ThrowsSchemaMismatch()
        {
            // tradetime и tradedate поменяны местами
            string json = """
            {
              "data": {
                "columns": ["tradetime","tradedate","secid","pr_open","pr_high","pr_low","pr_close","pr_std","vol","val","trades","pr_vwap","pr_change","trades_b","trades_s","val_b","val_s","vol_b","vol_s","disb","pr_vwap_b","pr_vwap_s","SYSTIME","sec_pr_open","sec_pr_high","sec_pr_low","sec_pr_close"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseTradeStatsStock(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 4. TradeStats Futures — short row → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradeStatsFutures_ShortRow_ThrowsSchemaMismatch()
        {
            // Строка содержит только 5 элементов вместо 33
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","pr_open","pr_high","pr_low","pr_close","pr_std","vol","val","trades","pr_vwap","pr_change","trades_b","trades_s","val_b","val_s","vol_b","vol_s","disb","pr_vwap_b","pr_vwap_s","im","oi_open","oi_high","oi_low","oi_close","sec_pr_open","sec_pr_high","sec_pr_low","sec_pr_close","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si",85000.0]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseTradeStatsFutures(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 5. TradeStats Stock — null values (pr_open=null, vol=null, SYSTIME=null)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradeStatsStock_NullValues_ParsedAsNull()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","pr_open","pr_high","pr_low","pr_close","pr_std","vol","val","trades","pr_vwap","pr_change","trades_b","trades_s","val_b","val_s","vol_b","vol_s","disb","pr_vwap_b","pr_vwap_s","SYSTIME","sec_pr_open","sec_pr_high","sec_pr_low","sec_pr_close"],
                "data": [
                  ["2026-05-05","10:05:00","SBER",null,301.0,299.0,300.8,0.5,null,300500.0,50,300.6,0.1,25,25,150250.0,150250.0,500,500,0.0,300.5,300.7,null,5,10,15,20]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesTradeStats5mDTO> result = ParsingAlgUtf8.ParseTradeStatsStock(bytes);

            Assert.Equal(1, result.Count);
            Assert.Null(result[0].PrOpen);
            Assert.Null(result[0].Vol);
            Assert.Null(result[0].SysTime);
            Assert.Equal("SBER", result[0].SecId);
        }
    }
}
