using History_DataMoex.Contracts.Dto.Iss;
using History_DataMoex.Parsing;
using History_DataMoex.Parsing.Errors;
using System.Text;

namespace TestHistoryData
{
    public class IssSecuritiesParserTests
    {
        // ═══════════════════════════════════════════════════════════
        // Тест 1: ISS Stock — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseIssSecurityStock_HappyPath_ReturnsParsedRows()
        {
            string json = """
            {
              "securities": {
                "columns": [
                  "SECID", "BOARDID", "SHORTNAME", "PREVPRICE", "LOTSIZE", "FACEVALUE",
                  "STATUS", "BOARDNAME", "DECIMALS", "SECNAME", "REMARKS", "MARKETCODE",
                  "INSTRID", "SECTORID", "MINSTEP", "PREVWAPRICE", "FACEUNIT", "PREVDATE",
                  "ISSUESIZE", "ISIN", "LATNAME", "REGNUMBER", "PREVLEGALCLOSEPRICE",
                  "CURRENCYID", "SECTYPE", "LISTLEVEL", "SETTLEDATE"
                ],
                "data": [
                  ["SBER", "TQBR", "Сбербанк", null, 10, 3.0, null, null, null, "Сбербанк России ПАО ао", null, "FNDT", null, null, null, null, null, "2026-05-05", null, null, null, null, 300.50, null, null, null, null],
                  ["SBER", "TQBR", "Сбербанк", null, 10, 3.0, null, null, null, "Сбербанк России ПАО ао", null, "FNDT", null, null, null, null, null, "2026-05-05", null, null, null, null, 300.50, null, null, null, null]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            List<StockSecurityDTO> result = ParsingIssUtf8.ParseIssSecurityStock(bytes);

            Assert.Equal(2, result.Count);

            StockSecurityDTO row = result[0];
            Assert.Equal("SBER", row.SECID);
            Assert.Equal("TQBR", row.BOARDID);
            Assert.Equal("Сбербанк", row.SHORTNAME);
            Assert.Equal(10, row.LOTSIZE);
            Assert.Equal(3.0, row.FACEVALUE);
            Assert.Equal("Сбербанк России ПАО ао", row.SECNAME);
            Assert.Equal("FNDT", row.MARKETCODE);
            Assert.Equal(new DateTime(2026, 5, 5), row.PREVDATE);
            Assert.Equal(300.50m, row.PREVLEGALCLOSEPRICE);
        }

        // ═══════════════════════════════════════════════════════════
        // Тест 2: ISS Futures — happy path
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseIssSecurityFutures_HappyPath_ReturnsParsedRows()
        {
            string json = """
            {
              "securities": {
                "columns": [
                  "SECID", "BOARDID", "SHORTNAME", "SECNAME", "PREVSETTLEPRICE", "DECIMALS",
                  "MINSTEP", "LASTTRADEDATE", "LASTDELDATE", "SECTYPE", "LATNAME", "ASSETCODE",
                  "PREVOPENPOSITION", "LOTVOLUME", "INITIALMARGIN", "HIGHLIMIT", "LOWLIMIT",
                  "STEPPRICE", "LASTSETTLEPRICE", "PREVPRICE", "IMTIME", "BUYSELLFEE",
                  "SCALPERFEE", "NEGOTIATEDFEE", "EXERCISEFEE", "OPENPOSITION"
                ],
                "data": [
                  ["SiM6", null, "SiM6", "Фьючерс Si", 85000.0, 2, 1.0, "2026-06-18", "2026-06-18", null, null, "Si", 100000, 1, 15000.0, 86000.0, 84000.0, 50.0, null, 84500.0, null, null, null, null, null, null],
                  ["SiM6", null, "SiM6", "Фьючерс Si", 85000.0, 2, 1.0, "2026-06-18", "2026-06-18", null, null, "Si", 100000, 1, 15000.0, 86000.0, 84000.0, 50.0, null, 84500.0, null, null, null, null, null, null]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            List<FuturesSecurityDTO> result = ParsingIssUtf8.ParseIssSecurityFutures(bytes);

            Assert.Equal(2, result.Count);

            FuturesSecurityDTO row = result[0];
            Assert.Equal("SiM6", row.SECID);
            Assert.Equal("SiM6", row.SHORTNAME);
            Assert.Equal("Фьючерс Si", row.SECNAME);
            Assert.Equal(85000.0, row.PREVSETTLEPRICE);
            Assert.Equal(2, row.DECIMALS);
            Assert.Equal(1.0, row.MINSTEP);
            Assert.Equal(new DateTime(2026, 6, 18), row.LASTTRADEDATE);
            Assert.Equal(new DateTime(2026, 6, 18), row.LASTDELDATE);
            Assert.Equal("Si", row.ASSETCODE);
            Assert.Equal(100000L, row.PREVOPENPOSITION);
            Assert.Equal(1, row.LOTVOLUME);
            Assert.Equal(15000.0, row.INITIALMARGIN);
            Assert.Equal(86000.0, row.HIGHLIMIT);
            Assert.Equal(84000.0, row.LOWLIMIT);
            Assert.Equal(50.0, row.STEPPRICE);
            Assert.Equal(84500.0, row.PREVPRICE);
        }

        // ═══════════════════════════════════════════════════════════
        // Тест 3: ISS Stock — переставленные колонки → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseIssSecurityStock_SwappedColumns_ThrowsSchemaMismatch()
        {
            // BOARDID и SECID переставлены местами (позиции 0 и 1)
            string json = """
            {
              "securities": {
                "columns": [
                  "BOARDID", "SECID", "SHORTNAME", "PREVPRICE", "LOTSIZE", "FACEVALUE",
                  "STATUS", "BOARDNAME", "DECIMALS", "SECNAME", "REMARKS", "MARKETCODE",
                  "INSTRID", "SECTORID", "MINSTEP", "PREVWAPRICE", "FACEUNIT", "PREVDATE",
                  "ISSUESIZE", "ISIN", "LATNAME", "REGNUMBER", "PREVLEGALCLOSEPRICE",
                  "CURRENCYID", "SECTYPE", "LISTLEVEL", "SETTLEDATE"
                ],
                "data": []
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingIssUtf8.ParseIssSecurityStock(bytes));
        }

        // ═══════════════════════════════════════════════════════════
        // Тест 4: ISS Futures — короткая строка → MoexSchemaMismatchException
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseIssSecurityFutures_ShortRow_ThrowsSchemaMismatch()
        {
            // Строка data содержит только 10 элементов вместо 26
            string json = """
            {
              "securities": {
                "columns": [
                  "SECID", "BOARDID", "SHORTNAME", "SECNAME", "PREVSETTLEPRICE", "DECIMALS",
                  "MINSTEP", "LASTTRADEDATE", "LASTDELDATE", "SECTYPE", "LATNAME", "ASSETCODE",
                  "PREVOPENPOSITION", "LOTVOLUME", "INITIALMARGIN", "HIGHLIMIT", "LOWLIMIT",
                  "STEPPRICE", "LASTSETTLEPRICE", "PREVPRICE", "IMTIME", "BUYSELLFEE",
                  "SCALPERFEE", "NEGOTIATEDFEE", "EXERCISEFEE", "OPENPOSITION"
                ],
                "data": [
                  ["SiM6", null, "SiM6", "Фьючерс Si", 85000.0, 2, 1.0, "2026-06-18", "2026-06-18", null]
                ]
              }
            }
            """;

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            Assert.Throws<MoexSchemaMismatchException>(
                () => ParsingIssUtf8.ParseIssSecurityFutures(bytes));
        }
    }
}
