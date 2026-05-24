using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Parsing;
using System.Text;

namespace TestHistoryData
{
    public class ParserOverloadCursorTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. TradeStatsStock — JSON с data.cursor → данные + cursor
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradeStatsStock_WithCursor_ReturnsBothDataAndCursor()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","pr_open","pr_high","pr_low","pr_close","pr_std","vol","val","trades","pr_vwap","pr_change","trades_b","trades_s","val_b","val_s","vol_b","vol_s","disb","pr_vwap_b","pr_vwap_s","SYSTIME","sec_pr_open","sec_pr_high","sec_pr_low","sec_pr_close"],
                "data": [
                  ["2026-05-05","10:05:00","SBER",300.5,301.0,299.0,300.8,0.5,1000,300500.0,50,300.6,0.1,25,25,150250.0,150250.0,500,500,0.0,300.5,300.7,"2026-05-05 10:05:01",5,10,15,20]
                ]
              },
              "data.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [[100, 5000, 100]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesTradeStats5mDTO> list = ParsingAlgUtf8.ParseTradeStatsStock(bytes, out PaginationCursorDTO cursor);

            Assert.Equal(1, list.Count);
            Assert.Equal(100, cursor.Index);
            Assert.Equal(5000, cursor.Total);
            Assert.Equal(100, cursor.PageSize);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Hi2Stock — JSON без data.cursor → данные + empty cursor
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseHi2Stock_WithoutCursor_ReturnsDataAndEmptyCursor()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","metric","value","reference","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SBER","hhi_volume",0.035,"","2026-05-05 10:05:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<Hi2AssetDTO> list = ParsingAlgUtf8.ParseHi2Stock(bytes, out PaginationCursorDTO cursor);

            Assert.Equal(1, list.Count);
            Assert.Null(cursor.Index);
            Assert.Null(cursor.Total);
            Assert.Null(cursor.PageSize);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. OBStatsFutures — с cursor, проверяем NextStart
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOBStatsFutures_WithCursor_CursorNextStartCorrect()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","mid_price","micro_price","spread_l1","spread_l2","spread_l3","spread_l5","spread_l10","spread_l20","levels_b","levels_s","vol_b_l1","vol_b_l2","vol_b_l3","vol_b_l5","vol_b_l10","vol_b_l20","vol_s_l1","vol_s_l2","vol_s_l3","vol_s_l5","vol_s_l10","vol_s_l20","vwap_b_l3","vwap_b_l5","vwap_b_l10","vwap_b_l20","vwap_s_l3","vwap_s_l5","vwap_s_l10","vwap_s_l20","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si",85000.0,85001.0,10.0,20.0,30.0,50.0,100.0,200.0,5,6,1000,2000,3000,5000,10000,20000,1100,2100,3100,5100,10100,20100,85010.0,85020.0,85030.0,85040.0,85050.0,85060.0,85070.0,85080.0,"2026-05-05 10:05:01"]
                ]
              },
              "data.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [[0, 10000, 1000]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<SuperCandlesFuturesOrderBookStats5mDTO> list = ParsingAlgUtf8.ParseOBStatsFutures(bytes, out PaginationCursorDTO cursor);

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 1, maxPagesGuard: 10000);

            Assert.Equal(1, list.Count);
            Assert.False(step.IsStop);
            Assert.Equal(1000, step.NextStart);
        }
    }
}
