using History_DataMoex.Contracts.Dto.Realtime;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class RealtimeParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. Orderbook Stock — happy path на raw fixture
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_Stock_Returns20Rows()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/orderbook-stock-raw.json");

            var result = ParsingRealtimeRestUtf8.ParseOrderbook(json);

            Assert.Equal(20, result.Rows.Count);

            // Первая строка — лучший bid
            Assert.Equal("TQBR", result.Rows[0].BoardId);
            Assert.Equal("SBER", result.Rows[0].SecId);
            Assert.Equal("B", result.Rows[0].BuySell);
            Assert.Equal(323.31, result.Rows[0].Price);
            Assert.Equal(2731L, result.Rows[0].Quantity);
            Assert.Equal(20260521203509L, result.Rows[0].SeqNum);
            Assert.Equal("20:35:09", result.Rows[0].UpdateTime);
            Assert.Equal(2L, result.Rows[0].Decimals);

            // Последняя строка — ask
            Assert.Equal("S", result.Rows[19].BuySell);
            Assert.Equal(323.56, result.Rows[19].Price);
            Assert.Equal(1539L, result.Rows[19].Quantity);

            // DataVersion
            Assert.Equal(8895, result.DataVersion.DataVersion);
            Assert.Equal(20260521203511L, result.DataVersion.SeqNum);
            Assert.Equal("2026-05-21", result.DataVersion.TradeDate);
            Assert.Equal("2026-05-21", result.DataVersion.TradeSessionDate);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Orderbook Futures — happy path на raw fixture
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_Futures_Returns40Rows()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/orderbook-futures-raw.json");

            var result = ParsingRealtimeRestUtf8.ParseOrderbook(json);

            Assert.Equal(40, result.Rows.Count);

            // Первая строка — bid
            Assert.Equal("RFUD", result.Rows[0].BoardId);
            Assert.Equal("SVM6", result.Rows[0].SecId);
            Assert.Equal("B", result.Rows[0].BuySell);
            Assert.Equal(77.56, result.Rows[0].Price);
            Assert.Equal(41L, result.Rows[0].Quantity);
            Assert.Equal(20260521203559L, result.Rows[0].SeqNum);

            // Последняя строка — ask
            Assert.Equal("S", result.Rows[39].BuySell);
            Assert.Equal(77.95, result.Rows[39].Price);
            Assert.Equal(57L, result.Rows[39].Quantity);

            // DataVersion
            Assert.Equal(13038, result.DataVersion.DataVersion);
            Assert.Equal(20260521203555L, result.DataVersion.SeqNum);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. Trades Stock — happy path на raw fixture
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradesStock_HappyPath_Returns5000Rows()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/trades-stock-raw.json");

            var result = ParsingRealtimeRestUtf8.ParseTradesStock(json);

            Assert.Equal(5000, result.Rows.Count);

            // Первая сделка
            Assert.Equal(16536717706L, result.Rows[0].TradeNo);
            Assert.Equal("06:59:53", result.Rows[0].TradeTime);
            Assert.Equal("TQBR", result.Rows[0].BoardId);
            Assert.Equal("SBER", result.Rows[0].SecId);
            Assert.Equal(323.4, result.Rows[0].Price);
            Assert.Equal(1L, result.Rows[0].Quantity);
            Assert.Equal(323.4, result.Rows[0].Value);
            Assert.Equal("S", result.Rows[0].Period);
            Assert.Equal(659, result.Rows[0].TradeTimeGrp);
            Assert.Equal("2026-05-21 06:59:53", result.Rows[0].SysTime);
            Assert.Equal("S", result.Rows[0].BuySell);
            Assert.Equal(2, result.Rows[0].Decimals);
            Assert.Equal("0", result.Rows[0].TradingSession);
            Assert.Equal("2026-05-21", result.Rows[0].TradeDate);
            Assert.Equal("2026-05-21", result.Rows[0].TradeSessionDate);

            // Последняя сделка
            Assert.Equal(16536905787L, result.Rows[4999].TradeNo);
            Assert.Equal("09:05:02", result.Rows[4999].TradeTime);
            Assert.Equal(323.5, result.Rows[4999].Price);
            Assert.Equal(651L, result.Rows[4999].Quantity);
            Assert.Equal(210598.5, result.Rows[4999].Value);
            Assert.Equal("N", result.Rows[4999].Period);

            // DataVersion
            Assert.Equal(8895, result.DataVersion.DataVersion);
            Assert.Equal(20260521203645L, result.DataVersion.SeqNum);
            Assert.Equal("2026-05-21", result.DataVersion.TradeDate);

            // TradesYields — пустой, но не null
            Assert.NotNull(result.Yields);
            Assert.Empty(result.Yields);
        }

        // ═══════════════════════════════════════════════════════════
        // 4. Trades Futures — happy path на raw fixture
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradesFutures_HappyPath_Returns5000Rows()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/trades-futures-raw.json");

            var result = ParsingRealtimeRestUtf8.ParseTradesFutures(json);

            Assert.Equal(5000, result.Rows.Count);

            // Первая сделка — проверяем 19-значный TRADENO
            Assert.Equal(1951779984234250241L, result.Rows[0].TradeNo);
            Assert.Equal("RFUD", result.Rows[0].BoardName);
            Assert.Equal("SVM6", result.Rows[0].SecId);
            Assert.Equal("2026-05-21", result.Rows[0].TradeDate);
            Assert.Equal("08:59:39", result.Rows[0].TradeTime);
            Assert.Equal(76.51, result.Rows[0].Price);
            Assert.Equal(2L, result.Rows[0].Quantity);
            Assert.Equal("2026-05-21 09:00:03", result.Rows[0].SysTime);
            Assert.Equal(322684262148L, result.Rows[0].RecNo);
            Assert.Equal(716810L, result.Rows[0].OpenPosition);
            Assert.Equal(0, result.Rows[0].OffMarketDeal);
            Assert.Equal("B", result.Rows[0].BuySell);
            Assert.Equal("2026-05-21", result.Rows[0].TradeSessionDate);

            // Последняя сделка
            Assert.Equal(1951779984234257291L, result.Rows[4999].TradeNo);
            Assert.Equal(76.27, result.Rows[4999].Price);
            Assert.Equal(322692650653L, result.Rows[4999].RecNo);
            Assert.Equal(720028L, result.Rows[4999].OpenPosition);
            Assert.Equal("S", result.Rows[4999].BuySell);

            // DataVersion
            Assert.Equal(13038, result.DataVersion.DataVersion);
            Assert.Equal(20260521203715L, result.DataVersion.SeqNum);

            // TradesYields — пустой
            Assert.NotNull(result.Yields);
            Assert.Empty(result.Yields);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. ParseDataVersion — standalone, из orderbook fixture
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseDataVersion_Stock_ReturnsCorrectValues()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/orderbook-stock-raw.json");

            var dv = ParsingRealtimeRestUtf8.ParseDataVersion(json);

            Assert.Equal(8895, dv.DataVersion);
            Assert.Equal(20260521203511L, dv.SeqNum);
            Assert.Equal("2026-05-21", dv.TradeDate);
            Assert.Equal("2026-05-21", dv.TradeSessionDate);
        }

        // ═══════════════════════════════════════════════════════════
        // 6. ParseTradesYields — standalone, из trades fixture
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradesYields_EmptyData_ReturnsEmptyList()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/trades-stock-raw.json");

            var yields = ParsingRealtimeRestUtf8.ParseTradesYields(json);

            Assert.NotNull(yields);
            Assert.Empty(yields);
        }

        // ═══════════════════════════════════════════════════════════
        // 7. Orderbook — стакан: первые 10 строк bid, следующие 10 ask (stock)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_Stock_BidThenAsk()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/orderbook-stock-raw.json");

            var result = ParsingRealtimeRestUtf8.ParseOrderbook(json);

            // Первые 10 строк — Buy
            for (int i = 0; i < 10; i++)
                Assert.Equal("B", result.Rows[i].BuySell);

            // Следующие 10 строк — Sell
            for (int i = 10; i < 20; i++)
                Assert.Equal("S", result.Rows[i].BuySell);
        }

        // ═══════════════════════════════════════════════════════════
        // 8. Trades Stock — первый TradeNo < последний TradeNo
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradesStock_TradeNoGrows()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/trades-stock-raw.json");

            var result = ParsingRealtimeRestUtf8.ParseTradesStock(json);

            Assert.True(result.Rows[0].TradeNo < result.Rows[4999].TradeNo,
                "TRADENO должен расти от первой к последней сделке");
        }

        // ═══════════════════════════════════════════════════════════
        // 9. Trades Futures — первый RecNo < последний RecNo
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradesFutures_RecNoGrows()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/trades-futures-raw.json");

            var result = ParsingRealtimeRestUtf8.ParseTradesFutures(json);

            Assert.True(result.Rows[0].RecNo < result.Rows[4999].RecNo,
                "RECNO должен расти от первой к последней сделке");
        }

        // ═══════════════════════════════════════════════════════════
        // 10. DataVersion — 0 строк → SchemaMismatch
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseDataVersion_EmptyData_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingRealtimeRestUtf8.ParseDataVersion(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 11. DataVersion — 2 строки → SchemaMismatch
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseDataVersion_TwoRows_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [
                  [100, 20260521120000, "2026-05-21", "2026-05-21"],
                  [101, 20260521120001, "2026-05-21", "2026-05-21"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingRealtimeRestUtf8.ParseDataVersion(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 12. Orderbook — data до columns → SchemaMismatch
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_DataBeforeColumns_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "orderbook": {
                "data": [
                  ["TQBR", "SBER", "B", 323.31, 2731, 20260521203509, "20:35:09", 2]
                ],
                "columns": ["BOARDID", "SECID", "BUYSELL", "PRICE", "QUANTITY", "SEQNUM", "UPDATETIME", "DECIMALS"]
              },
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [[100, 20260521120000, "2026-05-21", "2026-05-21"]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingRealtimeRestUtf8.ParseOrderbook(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 13. Orderbook — колонки перепутаны → SchemaMismatch
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_SwappedColumns_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "orderbook": {
                "columns": ["SECID", "BOARDID", "BUYSELL", "PRICE", "QUANTITY", "SEQNUM", "UPDATETIME", "DECIMALS"],
                "data": []
              },
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [[100, 20260521120000, "2026-05-21", "2026-05-21"]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingRealtimeRestUtf8.ParseOrderbook(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 14. Orderbook — лишняя колонка → SchemaMismatch
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_ExtraColumn_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "orderbook": {
                "columns": ["BOARDID", "SECID", "BUYSELL", "PRICE", "QUANTITY", "SEQNUM", "UPDATETIME", "DECIMALS", "EXTRA"],
                "data": []
              },
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [[100, 20260521120000, "2026-05-21", "2026-05-21"]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingRealtimeRestUtf8.ParseOrderbook(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 15. Orderbook — отсутствует root key → SchemaMismatch
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_MissingRootKey_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "unknown": {
                "columns": ["BOARDID"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingRealtimeRestUtf8.ParseOrderbook(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 16. Orderbook — null в Price → Price остаётся null
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_NullPrice_ParsedAsNull()
        {
            string json = """
            {
              "orderbook": {
                "columns": ["BOARDID", "SECID", "BUYSELL", "PRICE", "QUANTITY", "SEQNUM", "UPDATETIME", "DECIMALS"],
                "data": [
                  ["TQBR", "SBER", "B", null, 100, 20260521120000, "12:00:00", 2]
                ]
              },
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [[100, 20260521120000, "2026-05-21", "2026-05-21"]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            var result = ParsingRealtimeRestUtf8.ParseOrderbook(bytes);

            Assert.Equal(1, result.Rows.Count);
            Assert.Null(result.Rows[0].Price);
            Assert.Equal("TQBR", result.Rows[0].BoardId);
            Assert.Equal(100L, result.Rows[0].Quantity);
        }

        // ═══════════════════════════════════════════════════════════
        // 17. Orderbook — пустой data → 0 строк, dataversion валиден
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_EmptyData_ReturnsEmptyRows()
        {
            string json = """
            {
              "orderbook": {
                "columns": ["BOARDID", "SECID", "BUYSELL", "PRICE", "QUANTITY", "SEQNUM", "UPDATETIME", "DECIMALS"],
                "data": []
              },
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [[100, 20260521120000, "2026-05-21", "2026-05-21"]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            var result = ParsingRealtimeRestUtf8.ParseOrderbook(bytes);

            Assert.Empty(result.Rows);
            Assert.Equal(100, result.DataVersion.DataVersion);
        }

        // ═══════════════════════════════════════════════════════════
        // 18. Trades Stock — короткая строка data → SchemaMismatch
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseTradesStock_ShortRow_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "trades": {
                "columns": ["TRADENO","TRADETIME","BOARDID","SECID","PRICE","QUANTITY","VALUE","PERIOD","TRADETIME_GRP","SYSTIME","BUYSELL","DECIMALS","TRADINGSESSION","TRADEDATE","TRADE_SESSION_DATE"],
                "data": [
                  [100, "10:00:00", "TQBR", "SBER", 300.0]
                ]
              },
              "dataversion": {
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [[100, 20260521120000, "2026-05-21", "2026-05-21"]]
              },
              "trades_yields": {
                "columns": ["boardid", "secid"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingRealtimeRestUtf8.ParseTradesStock(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 19. metadata блок не ломает парсинг
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOrderbook_MetadataPresent_SkippedCorrectly()
        {
            string json = """
            {
              "orderbook": {
                "metadata": {
                  "BOARDID": {"type": "string", "bytes": 12},
                  "SECID": {"type": "string", "bytes": 36}
                },
                "columns": ["BOARDID", "SECID", "BUYSELL", "PRICE", "QUANTITY", "SEQNUM", "UPDATETIME", "DECIMALS"],
                "data": [
                  ["TQBR", "SBER", "B", 300.0, 100, 20260521120000, "12:00:00", 2]
                ]
              },
              "dataversion": {
                "metadata": {
                  "data_version": {"type": "int32"}
                },
                "columns": ["data_version", "seqnum", "trade_date", "trade_session_date"],
                "data": [[100, 20260521120000, "2026-05-21", "2026-05-21"]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            var result = ParsingRealtimeRestUtf8.ParseOrderbook(bytes);

            Assert.Equal(1, result.Rows.Count);
            Assert.Equal("TQBR", result.Rows[0].BoardId);
            Assert.Equal(100, result.DataVersion.DataVersion);
        }
    }
}
