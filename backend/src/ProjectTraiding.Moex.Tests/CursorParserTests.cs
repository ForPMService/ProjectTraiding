using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class CursorParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. Happy path — cursor "data.cursor" → INDEX=0, TOTAL=5000, PAGESIZE=100
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseCursorUtf8_DataCursorKey_ReturnsCorrectValues()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate", "tradetime"],
                "data": []
              },
              "data.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [[0, 5000, 100]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            PaginationCursorDTO result = ParseHelpersUtf8.ParseCursorUtf8(bytes, "data.cursor");

            Assert.Equal(0, result.Index);
            Assert.Equal(5000, result.Total);
            Assert.Equal(100, result.PageSize);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Другой cursorKey — "suspended.cursor"
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseCursorUtf8_SuspendedCursorKey_ReturnsCorrectValues()
        {
            string json = """
            {
              "suspended": {
                "columns": ["secid", "reason_id"],
                "data": []
              },
              "suspended.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [[200, 168000, 100]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            PaginationCursorDTO result = ParseHelpersUtf8.ParseCursorUtf8(bytes, "suspended.cursor");

            Assert.Equal(200, result.Index);
            Assert.Equal(168000, result.Total);
            Assert.Equal(100, result.PageSize);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. Cursor-блок не найден → MoexSchemaMismatchException (Lock §10)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseCursorUtf8_CursorBlockMissing_ThrowsSchemaMismatch()
        {
            string json = """
            {
              "data": {
                "columns": ["tradedate", "tradetime"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParseHelpersUtf8.ParseCursorUtf8(bytes, "data.cursor"));
        }

        // ═══════════════════════════════════════════════════════════
        // 4. Пустой data[] → все поля null
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseCursorUtf8_EmptyDataArray_ReturnsNullFields()
        {
            string json = """
            {
              "data.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            PaginationCursorDTO result = ParseHelpersUtf8.ParseCursorUtf8(bytes, "data.cursor");

            Assert.Null(result.Index);
            Assert.Null(result.Total);
            Assert.Null(result.PageSize);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. securities.cursor ключ → корректные значения
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseCursorUtf8_SecuritiesCursorKey_ReturnsCorrectValues()
        {
            string json = """
            {
              "securities": { "columns": ["updatetime"], "data": [] },
              "securities.cursor": {
                "columns": ["INDEX", "TOTAL", "PAGESIZE"],
                "data": [[300, 12000, 100]]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            PaginationCursorDTO result = ParseHelpersUtf8.ParseCursorUtf8(bytes, "securities.cursor");

            Assert.Equal(300, result.Index);
            Assert.Equal(12000, result.Total);
            Assert.Equal(100, result.PageSize);
        }
    }
}
