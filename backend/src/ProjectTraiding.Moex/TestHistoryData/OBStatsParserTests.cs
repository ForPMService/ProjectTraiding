using History_DataMoex.Contracts.Dto.Algopack;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class OBStatsParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. OBStats Stock — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOBStatsStock_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","spread_bbo","spread_lv10","spread_1mio","levels_b","levels_s","vol_b","vol_s","val_b","val_s","imbalance_vol_bbo","imbalance_val_bbo","imbalance_vol","imbalance_val","vwap_b","vwap_s","vwap_b_1mio","vwap_s_1mio","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SBER",0.05,0.15,0.25,10,12,50000,45000,15000000,13500000,0.11,0.12,0.05,0.06,300.5,300.7,300.4,300.8,"2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SBER",0.06,0.16,0.26,11,13,51000,46000,15300000,13800000,0.12,0.13,0.06,0.07,300.6,300.8,300.5,300.9,"2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesOrderBookStats5mDTO> result = ParsingAlgUtf8.ParseOBStatsStock(bytes);

            Assert.Equal(2, result.Count);

            Assert.Equal("2026-05-05", result[0].TradeDate);
            Assert.Equal(0.05, result[0].SpreadBbo);
            Assert.Equal(10, result[0].LevelsB);
            Assert.Equal(50000L, result[0].VolB);
            Assert.Equal(300.5, result[0].VwapB);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. OBStats Futures — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOBStatsFutures_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","mid_price","micro_price","spread_l1","spread_l2","spread_l3","spread_l5","spread_l10","spread_l20","levels_b","levels_s","vol_b_l1","vol_b_l2","vol_b_l3","vol_b_l5","vol_b_l10","vol_b_l20","vol_s_l1","vol_s_l2","vol_s_l3","vol_s_l5","vol_s_l10","vol_s_l20","vwap_b_l3","vwap_b_l5","vwap_b_l10","vwap_b_l20","vwap_s_l3","vwap_s_l5","vwap_s_l10","vwap_s_l20","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si",85000.0,85001.0,10.0,20.0,30.0,50.0,100.0,200.0,5,6,1000,2000,3000,5000,10000,20000,1100,2100,3100,5100,10100,20100,85010.0,85020.0,85030.0,85040.0,85050.0,85060.0,85070.0,85080.0,"2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SiM6","Si",85010.0,85011.0,11.0,21.0,31.0,51.0,101.0,201.0,6,7,1001,2001,3001,5001,10001,20001,1101,2101,3101,5101,10101,20101,85011.0,85021.0,85031.0,85041.0,85051.0,85061.0,85071.0,85081.0,"2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesFuturesOrderBookStats5mDTO> result = ParsingAlgUtf8.ParseOBStatsFutures(bytes);

            Assert.Equal(2, result.Count);

            Assert.Equal("Si", result[0].AssetCode);
            Assert.Equal(85000.0, result[0].MidPrice);
            Assert.Equal(10.0, result[0].SpreadL1);
            Assert.Equal(1000L, result[0].VolBL1);
            Assert.Equal(85010.0, result[0].VwapBL3);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. OBStats Stock — swapped columns → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOBStatsStock_SwappedColumns_ThrowsSchemaMismatch()
        {
            // tradetime и tradedate поменяны местами
            string json = """
            {
              "data": {
                "columns": ["tradetime","tradedate","secid","spread_bbo","spread_lv10","spread_1mio","levels_b","levels_s","vol_b","vol_s","val_b","val_s","imbalance_vol_bbo","imbalance_val_bbo","imbalance_vol","imbalance_val","vwap_b","vwap_s","vwap_b_1mio","vwap_s_1mio","SYSTIME"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseOBStatsStock(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 4. OBStats Futures — short row → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOBStatsFutures_ShortRow_ThrowsSchemaMismatch()
        {
            // Строка содержит только 5 элементов вместо 35
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","mid_price","micro_price","spread_l1","spread_l2","spread_l3","spread_l5","spread_l10","spread_l20","levels_b","levels_s","vol_b_l1","vol_b_l2","vol_b_l3","vol_b_l5","vol_b_l10","vol_b_l20","vol_s_l1","vol_s_l2","vol_s_l3","vol_s_l5","vol_s_l10","vol_s_l20","vwap_b_l3","vwap_b_l5","vwap_b_l10","vwap_b_l20","vwap_s_l3","vwap_s_l5","vwap_s_l10","vwap_s_l20","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si",85000.0]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseOBStatsFutures(bytes));
        }
    }
}
