using History_DataMoex.Contracts.Dto.Algopack;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using System.Text;
using System.Text.Json;

namespace TestHistoryData
{
    public class CandlesParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. Happy path — парсим golden файл, проверяем значения
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_HappyPath_Returns5Candles()
        {
            byte[] json = File.ReadAllBytes("TestFixtures/candles_happy.json");

            List<CandlesDTO> result = ParsingAlgUtf8.ParseAlgCandles(json);

            Assert.Equal(5, result.Count);

            // Первая свеча
            Assert.Equal(100.5, result[0].Open);
            Assert.Equal(101.0, result[0].Close);
            Assert.Equal(102.0, result[0].High);
            Assert.Equal(99.5, result[0].Low);
            Assert.Equal(5000000.0, result[0].Value);
            Assert.Equal(50000, result[0].Volume);
            Assert.Equal(new DateTime(2026, 4, 30, 10, 0, 0), result[0].Begin);
            Assert.Equal(new DateTime(2026, 4, 30, 10, 0, 59), result[0].End);

            // Последняя свеча
            Assert.Equal(105.0, result[4].Open);
            Assert.Equal(104.5, result[4].Close);
            Assert.Equal(new DateTime(2026, 4, 30, 10, 4, 0), result[4].Begin);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Missing column — убрана колонка "volume"
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_MissingColumn_ThrowsSchemaMismatch()
        {
            // 7 колонок вместо 8 — "volume" убрана
            string json = """
            {
              "candles": {
                "columns": ["open", "close", "high", "low", "value", "begin", "end"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseAlgCandles(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 3. Swapped columns — "open" и "close" поменяны местами
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_SwappedColumns_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "candles": {
                "columns": ["close", "open", "high", "low", "value", "volume", "begin", "end"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseAlgCandles(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 4. Extra column — 9 колонок вместо 8
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_ExtraColumn_ThrowsError()
        {
            string json = """
            {
              "candles": {
                "columns": ["open", "close", "high", "low", "value", "volume", "begin", "end", "extra"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            // Phase 8-A: column count mismatch — structural → MoexSchemaMismatchException (Lock §10).
            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseAlgCandles(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 5. Short row (A4) — строка data короче 8 элементов
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_ShortRow_ThrowsSchemaMismatch()
        {
            // Строка data содержит только 5 элементов вместо 8
            string json = """
            {
              "candles": {
                "columns": ["open", "close", "high", "low", "value", "volume", "begin", "end"],
                "data": [
                  [100.5, 101.0, 102.0, 99.5, 5000000.0]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingAlgUtf8.ParseAlgCandles(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // 6. Data before columns (A2) — data идёт до columns
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_DataBeforeColumns_ThrowsError()
        {
            string json = """
            {
              "candles": {
                "data": [
                  [100.5, 101.0, 102.0, 99.5, 5000000.0, 50000, "2026-04-30 10:00:00", "2026-04-30 10:00:59"]
                ],
                "columns": ["open", "close", "high", "low", "value", "volume", "begin", "end"]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            var ex = Assert.Throws<MoexSchemaMismatchException>(() =>
                ParsingAlgUtf8.ParseAlgCandles(bytes));

            Assert.Contains("candles", ex.Message);
            Assert.Contains("data", ex.Message);
        }

        // ═══════════════════════════════════════════════════════════
        // 7. Null values — open=null, low=null
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_NullValues_ParsedAsNull()
        {
            string json = """
            {
              "candles": {
                "columns": ["open","close","high","low","value","volume","begin","end"],
                "data": [
                  [null, 101.0, 102.0, null, 5000000.0, 50000.0, "2026-04-30 10:00:00", "2026-04-30 10:00:59"]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<CandlesDTO> result = ParsingAlgUtf8.ParseAlgCandles(bytes);

            Assert.Equal(1, result.Count);
            Assert.Null(result[0].Open);
            Assert.Null(result[0].Low);
            Assert.Equal(101.0, result[0].Close);
        }

        // ═══════════════════════════════════════════════════════════
        // 8. Пустой data → пустой list
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAlgCandles_EmptyData_ReturnsEmptyList()
        {
            string json = """
            {
              "candles": {
                "columns": ["open","close","high","low","value","volume","begin","end"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            List<CandlesDTO> result = ParsingAlgUtf8.ParseAlgCandles(bytes);

            Assert.Equal(0, result.Count);
        }
    }
}
