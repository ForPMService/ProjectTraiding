// LEGACY: not used by Phase 8 mappers. Production path is ParsingIssUtf8.
// Removal: separate cleanup task after Phase 8-D. Lock §11.
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParsingISS
    {
        // HISTORICAL: kept for source-contract audit. All callers migrated to Utf8 parsers (B9.5 / B10).
        // Uncomment only if need to compare JsonDocument vs Utf8JsonReader output.
        /*

        public static List<StockSecurityDTO> ParseIssSecurityStock(JsonDocument jsonDocument)
        {
            List<StockSecurityDTO> stockSecurities = new List<StockSecurityDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement securities = root.GetProperty("securities");
            JsonElement columns = securities.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns);

           
            JsonElement datas = securities.GetProperty("data");
            for(int i =0; i < datas.GetArrayLength();i++)
            {
                
                StockSecurityDTO stockSecurityDTO = new StockSecurityDTO
                {
                    SECID = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[0].SourceIndex]),
                    SHORTNAME = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[2].SourceIndex]),
                    SECNAME = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[5].SourceIndex]),
                    BOARDID = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[1].SourceIndex]),
                    PREVLEGALCLOSEPRICE = ParseHelpers.GetDecimalOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[8].SourceIndex]),
                    LOTSIZE = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[3].SourceIndex]),
                    FACEVALUE = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[4].SourceIndex]),
                    MARKETCODE = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[6].SourceIndex]),
                    PREVDATE = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.IssStockSecurityExpectedColumns[7].SourceIndex])
                };
                stockSecurities.Add(stockSecurityDTO);
            }

            return stockSecurities;

        }

        public static List<FuturesSecurityDTO> ParseIssSecurityFutures(JsonDocument jsonDocument)
        {
            List<FuturesSecurityDTO> futuresSecurities = new List<FuturesSecurityDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement securities = root.GetProperty("securities");
            JsonElement columns = securities.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns);

            JsonElement datas = securities.GetProperty("data");
            for (int i = 0; i < datas.GetArrayLength(); i++)
            {

                FuturesSecurityDTO futuresSecurityDTO = new FuturesSecurityDTO
                {
                    SECID = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[0].SourceIndex]),
                    SHORTNAME = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[1].SourceIndex]),
                    SECNAME = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[2].SourceIndex]),
                    ASSETCODE = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[8].SourceIndex]),
                    INITIALMARGIN = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[11].SourceIndex]),
                    PREVSETTLEPRICE = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[3].SourceIndex]),
                    PREVPRICE = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[15].SourceIndex]),
                    MINSTEP = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[5].SourceIndex]),
                    STEPPRICE = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[14].SourceIndex]),
                    LOTVOLUME = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[10].SourceIndex]),
                    LASTTRADEDATE = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[6].SourceIndex]),
                    LASTDELDATE = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[7].SourceIndex]),
                    PREVOPENPOSITION = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[9].SourceIndex]),
                    HIGHLIMIT = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[12].SourceIndex]),
                    LOWLIMIT = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[13].SourceIndex]),
                    DECIMALS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.IssFuturesSecurityExpectedColumns[4].SourceIndex])
                };
                futuresSecurities.Add(futuresSecurityDTO);
            }

            return futuresSecurities;

        }
        */

    }
}
