using History_DataMoex.Contracts.Dto.Algopack;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class Hi2AndAlertsParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. HI2 Stock — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseHi2Stock_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","metric","value","reference","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SBER","hhi_volume",0.035,"","2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SBER","hhi_buy",0.041,"","2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<Hi2AssetDTO> result = ParsingAlgUtf8.ParseHi2Stock(bytes);

            Assert.Equal(2, result.Count);
            Assert.Equal("2026-05-05", result[0].TradeDate);
            Assert.Equal("SBER", result[0].SecId);
            Assert.Equal("hhi_volume", result[0].Metric);
            Assert.Equal(0.035, result[0].Value);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. HI2 Futures — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseHi2Futures_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","metric","value","reference","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si","hhi_agressive",0.042,"","2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SiM6","Si","hhi_sell",0.038,"","2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<Hi2FuturesDTO> result = ParsingAlgUtf8.ParseHi2Futures(bytes);

            Assert.Equal(2, result.Count);
            Assert.Equal("Si", result[0].AssetCode);
            Assert.Equal("hhi_agressive", result[0].Metric);
            Assert.Equal(0.042, result[0].Value);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. MegaAlerts Stock — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseMegaAlertsStock_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","alert_type","threshold","value","reference","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SBER","vol_99_9_pctl",1500000.0,2000000.0,"{}","2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SBER","net_vol_99_9_pctl-",800000.0,950000.0,"{}","2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<MegaAlertsAssetsDTO> result = ParsingAlgUtf8.ParseMegaAlertsStock(bytes);

            Assert.Equal(2, result.Count);
            Assert.Equal("vol_99_9_pctl", result[0].AlertType);
            Assert.Equal(1500000.0, result[0].Threshold);
            Assert.Equal(2000000.0, result[0].Value);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
        }

        // ═══════════════════════════════════════════════════════════
        // 4. MegaAlerts Futures — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseMegaAlertsFutures_HappyPath_Returns2Rows()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","alert_type","threshold","value","reference","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si","pr_change_99_9_pctl-",500.0,550.0,"{}","2026-05-05 10:05:01"],
                  ["2026-05-05","10:10:00","SiM6","Si","vol_99_9_pctl",100000.0,120000.0,"{}","2026-05-05 10:10:01"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<MegaAlertsFuturesDTO> result = ParsingAlgUtf8.ParseMegaAlertsFutures(bytes);

            Assert.Equal(2, result.Count);
            Assert.Equal("Si", result[0].AssetCode);
            Assert.Equal("pr_change_99_9_pctl-", result[0].AlertType);
            Assert.Equal(500.0, result[0].Threshold);
            Assert.Equal(550.0, result[0].Value);
            Assert.Equal(new DateTime(2026, 5, 5, 10, 5, 1), result[0].SysTime);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. HI2 Stock — swapped columns → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseHi2Stock_SwappedColumns_ThrowsSchemaMismatch()
        {
            // tradetime и tradedate поменяны местами
            string json = """
            {
              "data": {
                "columns": ["tradetime","tradedate","secid","metric","value","reference","SYSTIME"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseHi2Stock(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 6. MegaAlerts Futures — short row → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseMegaAlertsFutures_ShortRow_ThrowsSchemaMismatch()
        {
            // Строка содержит только 4 элемента вместо 9
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","asset_code","alert_type","threshold","value","reference","SYSTIME"],
                "data": [
                  ["2026-05-05","10:05:00","SiM6","Si"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseMegaAlertsFutures(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 7. MegaAlerts Stock — пустой data → пустой list
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseMegaAlertsStock_EmptyData_ReturnsEmptyList()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate","tradetime","secid","alert_type","threshold","value","reference","SYSTIME"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<MegaAlertsAssetsDTO> result = ParsingAlgUtf8.ParseMegaAlertsStock(bytes);

            Assert.Equal(0, result.Count);
        }
    }
}
