using History_DataMoex.Contracts.Dto;
using History_DataMoex.Contracts.Dto.Algopack;

using System.Text.Json;

namespace History_DataMoex.Parsing
{
    public static class ParsingALG
    {
        // HISTORICAL: kept for source-contract audit. All callers migrated to Utf8 parsers (B9.5 / B10).
        // Uncomment only if need to compare JsonDocument vs Utf8JsonReader output.
        /*
        public static List<CandlesDTO> ParseAlgCandles(JsonDocument jsonDocument)
        {
            List<CandlesDTO> candlesList = new List<CandlesDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement candles = root.GetProperty("candles");
            JsonElement columns = candles.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.AlgCandlesExpectedColumns);


            JsonElement datas = candles.GetProperty("data");
            for (int i = 0; i < datas.GetArrayLength(); i++)
            {

                CandlesDTO candlesDTO = new CandlesDTO()
                {
                    Open = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[0].SourceIndex]),
                    Close = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[1].SourceIndex]),
                    High = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[2].SourceIndex]),
                    Low = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[3].SourceIndex]),
                    Value = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[4].SourceIndex]),
                    Volume = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[5].SourceIndex]),
                    Begin = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[6].SourceIndex]),
                    End = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesExpectedColumns[7].SourceIndex])
                };
                candlesList.Add(candlesDTO);
            }

            return candlesList;

        }

        public static List<SuperCandlesTradeStats5mDTO> ParseAlgCandlesTradeStat(JsonDocument jsonDocument)
        {
            List<SuperCandlesTradeStats5mDTO> tradeStatList = new List<SuperCandlesTradeStats5mDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns);


            JsonElement datas = data.GetProperty("data");
            for (int i = 0; i < datas.GetArrayLength(); i++)
            {

                SuperCandlesTradeStats5mDTO tradeStatsDTO = new SuperCandlesTradeStats5mDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[2].SourceIndex]),

                    PrOpen = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[3].SourceIndex]),
                    PrHigh = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[4].SourceIndex]),
                    PrLow = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[5].SourceIndex]),
                    PrClose = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[6].SourceIndex]),

                    PrStd = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[7].SourceIndex]),

                    Vol = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[8].SourceIndex]),
                    Val = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[9].SourceIndex]),
                    Trades = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[10].SourceIndex]),

                    PrVwap = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[11].SourceIndex]),
                    PrChange = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[12].SourceIndex]),

                    TradesB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[13].SourceIndex]),
                    TradesS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[14].SourceIndex]),

                    ValB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[15].SourceIndex]),
                    ValS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[16].SourceIndex]),

                    VolB = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[17].SourceIndex]),
                    VolS = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[18].SourceIndex]),

                    Disb = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[19].SourceIndex]),

                    PrVwapB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[20].SourceIndex]),
                    PrVwapS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[21].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[22].SourceIndex]),

                    SecPrOpen = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[23].SourceIndex]),
                    SecPrHigh = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[24].SourceIndex]),
                    SecPrLow = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[25].SourceIndex]),
                    SecPrClose = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgCandlesTradeStatExpectedColumns[26].SourceIndex])
                };
                tradeStatList.Add(tradeStatsDTO);
            }

            return tradeStatList;

        }

        public static List<SuperCandlesFuturesOrderBookStats5mDTO> ParseAlgFuturesOrderBook(JsonDocument jsonDocument)
        {
            List<SuperCandlesFuturesOrderBookStats5mDTO> orderBookStatsList = new List<SuperCandlesFuturesOrderBookStats5mDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns);

            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                SuperCandlesFuturesOrderBookStats5mDTO orderBookStatsDTO = new SuperCandlesFuturesOrderBookStats5mDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[2].SourceIndex]),
                    AssetCode = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[3].SourceIndex]),

                    MidPrice = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[4].SourceIndex]),
                    MicroPrice = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[5].SourceIndex]),

                    SpreadL1 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[6].SourceIndex]),
                    SpreadL2 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[7].SourceIndex]),
                    SpreadL3 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[8].SourceIndex]),
                    SpreadL5 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[9].SourceIndex]),
                    SpreadL10 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[10].SourceIndex]),
                    SpreadL20 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[11].SourceIndex]),

                    LevelsB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[12].SourceIndex]),
                    LevelsS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[13].SourceIndex]),

                    VolBL1 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[14].SourceIndex]),
                    VolBL2 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[15].SourceIndex]),
                    VolBL3 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[16].SourceIndex]),
                    VolBL5 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[17].SourceIndex]),
                    VolBL10 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[18].SourceIndex]),
                    VolBL20 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[19].SourceIndex]),

                    VolSL1 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[20].SourceIndex]),
                    VolSL2 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[21].SourceIndex]),
                    VolSL3 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[22].SourceIndex]),
                    VolSL5 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[23].SourceIndex]),
                    VolSL10 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[24].SourceIndex]),
                    VolSL20 = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[25].SourceIndex]),

                    VwapBL3 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[26].SourceIndex]),
                    VwapBL5 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[27].SourceIndex]),
                    VwapBL10 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[28].SourceIndex]),
                    VwapBL20 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[29].SourceIndex]),

                    VwapSL3 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[30].SourceIndex]),
                    VwapSL5 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[31].SourceIndex]),
                    VwapSL10 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[32].SourceIndex]),
                    VwapSL20 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[33].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.AlgFuturesOrderBookExpectedColumns[34].SourceIndex])
                };

                orderBookStatsList.Add(orderBookStatsDTO);
            }

            return orderBookStatsList;
        }

        public static List<SuperCandlesOrderBookStats5mDTO> ParseAlgOrderBookStats5m(JsonDocument jsonDocument)
        {
            List<SuperCandlesOrderBookStats5mDTO> orderBookStatsList = new List<SuperCandlesOrderBookStats5mDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns);

            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                SuperCandlesOrderBookStats5mDTO orderBookStatsDTO = new SuperCandlesOrderBookStats5mDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[2].SourceIndex]),

                    SpreadBbo = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[3].SourceIndex]),
                    SpreadLv10 = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[4].SourceIndex]),
                    Spread1Mio = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[5].SourceIndex]),

                    LevelsB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[6].SourceIndex]),
                    LevelsS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[7].SourceIndex]),

                    VolB = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[8].SourceIndex]),
                    VolS = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[9].SourceIndex]),
                    ValB = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[10].SourceIndex]),
                    ValS = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[11].SourceIndex]),

                    ImbalanceVolBbo = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[12].SourceIndex]),
                    ImbalanceValBbo = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[13].SourceIndex]),
                    ImbalanceVol = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[14].SourceIndex]),
                    ImbalanceVal = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[15].SourceIndex]),

                    VwapB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[16].SourceIndex]),
                    VwapS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[17].SourceIndex]),
                    VwapB1Mio = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[18].SourceIndex]),
                    VwapS1Mio = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[19].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderBookStats5mExpectedColumns[20].SourceIndex])
                };

                orderBookStatsList.Add(orderBookStatsDTO);
            }

            return orderBookStatsList;
        }

        public static List<SuperCandlesFuturesTradeStats5mDTO> ParseFuturesTradeStats(JsonDocument jsonDocument)
        {
            List<SuperCandlesFuturesTradeStats5mDTO> tradeStatsList = new List<SuperCandlesFuturesTradeStats5mDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns);

            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                SuperCandlesFuturesTradeStats5mDTO tradeStatsDTO = new SuperCandlesFuturesTradeStats5mDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[2].SourceIndex]),
                    AssetCode = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[3].SourceIndex]),

                    PrOpen = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[4].SourceIndex]),
                    PrHigh = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[5].SourceIndex]),
                    PrLow = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[6].SourceIndex]),
                    PrClose = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[7].SourceIndex]),
                    PrStd = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[8].SourceIndex]),

                    Vol = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[9].SourceIndex]),
                    Val = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[10].SourceIndex]),
                    Trades = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[11].SourceIndex]),

                    PrVwap = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[12].SourceIndex]),
                    PrChange = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[13].SourceIndex]),

                    TradesB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[14].SourceIndex]),
                    TradesS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[15].SourceIndex]),

                    ValB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[16].SourceIndex]),
                    ValS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[17].SourceIndex]),

                    VolB = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[18].SourceIndex]),
                    VolS = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[19].SourceIndex]),

                    Disb = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[20].SourceIndex]),

                    PrVwapB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[21].SourceIndex]),
                    PrVwapS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[22].SourceIndex]),

                    Im = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[23].SourceIndex]),

                    OiOpen = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[24].SourceIndex]),
                    OiHigh = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[25].SourceIndex]),
                    OiLow = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[26].SourceIndex]),
                    OiClose = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[27].SourceIndex]),

                    SecPrOpen = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[28].SourceIndex]),
                    SecPrHigh = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[29].SourceIndex]),
                    SecPrLow = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[30].SourceIndex]),
                    SecPrClose = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[31].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.FuturesTradeStatsExpectedColumns[32].SourceIndex])
                };

                tradeStatsList.Add(tradeStatsDTO);
            }

            return tradeStatsList;
        }

        public static List<FutoiDTO> ParseFutoi(JsonDocument jsonDocument)
        {
            List<FutoiDTO> futoiList = new List<FutoiDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement futoi = root.GetProperty("futoi");
            JsonElement columns = futoi.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.FutoiExpectedColumns);

            JsonElement datas = futoi.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                FutoiDTO futoiDTO = new FutoiDTO()
                {
                    SessId = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[0].SourceIndex]),
                    SeqNum = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[1].SourceIndex]),

                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[2].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[3].SourceIndex]),

                    Ticker = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[4].SourceIndex]),
                    ClGroup = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[5].SourceIndex]),

                    Pos = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[6].SourceIndex]),
                    PosLong = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[7].SourceIndex]),
                    PosShort = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[8].SourceIndex]),

                    PosLongNum = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[9].SourceIndex]),
                    PosShortNum = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[10].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[11].SourceIndex]),
                    TradeSessionDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.FutoiExpectedColumns[12].SourceIndex])
                };

                futoiList.Add(futoiDTO);
            }

            return futoiList;
        }

        public static List<Hi2AssetDTO> ParseHi2Assets(JsonDocument jsonDocument)
        {
            

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");

            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.Hi2AssetExpectedColumns);
            List<Hi2AssetDTO> hi2AssetList = new List<Hi2AssetDTO>();
            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                Hi2AssetDTO hi2AssetDTO = new Hi2AssetDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2AssetExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2AssetExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2AssetExpectedColumns[2].SourceIndex]),

                    Metric = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2AssetExpectedColumns[3].SourceIndex]),
                    Value = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.Hi2AssetExpectedColumns[4].SourceIndex]),
                    Reference = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2AssetExpectedColumns[5].SourceIndex]),
                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.Hi2AssetExpectedColumns[6].SourceIndex])
                };

                hi2AssetList.Add(hi2AssetDTO);
            }

            return hi2AssetList;
        }



        

        public static List<Hi2FuturesDTO> ParseHi2Futures(JsonDocument jsonDocument)
        {
            List<Hi2FuturesDTO> hi2FuturesList = new List<Hi2FuturesDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns);

            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                Hi2FuturesDTO hi2FuturesDTO = new Hi2FuturesDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[1].SourceIndex]),

                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[2].SourceIndex]),
                    AssetCode = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[3].SourceIndex]),

                    Metric = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[4].SourceIndex]),
                    Value = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[5].SourceIndex]),
                    Reference = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[6].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.Hi2FuturesExpectedColumns[7].SourceIndex])
                };

                hi2FuturesList.Add(hi2FuturesDTO);
            }

            return hi2FuturesList;
        }

        public static List<MegaAlertsAssetsDTO> ParseMegaAlerts(JsonDocument jsonDocument)
        {
            List<MegaAlertsAssetsDTO> megaAlertsList = new List<MegaAlertsAssetsDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns);

            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                MegaAlertsAssetsDTO megaAlertsDTO = new MegaAlertsAssetsDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[2].SourceIndex]),

                    AlertType = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[3].SourceIndex]),
                    Threshold = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[4].SourceIndex]),
                    Value = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[5].SourceIndex]),
                    Reference = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[6].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsAssetExpectedColumns[7].SourceIndex])
                };

                megaAlertsList.Add(megaAlertsDTO);
            }

            return megaAlertsList;
        }
        public static List<MegaAlertsFuturesDTO> ParseMegaAlertsFutures(JsonDocument jsonDocument)
        {
            List<MegaAlertsFuturesDTO> megaAlertsFuturesList = new List<MegaAlertsFuturesDTO>();

            

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns);

            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                MegaAlertsFuturesDTO megaAlertsFuturesDTO = new MegaAlertsFuturesDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[1].SourceIndex]),

                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[2].SourceIndex]),
                    AssetCode = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[3].SourceIndex]),

                    AlertType = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[4].SourceIndex]),
                    Threshold = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[5].SourceIndex]),
                    Value = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[6].SourceIndex]),
                    Reference = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[7].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.MegaAlertsFuturesExpectedColumns[8].SourceIndex])
                };

                megaAlertsFuturesList.Add(megaAlertsFuturesDTO);
            }

            return megaAlertsFuturesList;
        }
        public static List<SuperCandlesOrderStats5mDTO> ParseAlgOrderStats5m(JsonDocument jsonDocument)
        {
            List<SuperCandlesOrderStats5mDTO> orderStatsList = new List<SuperCandlesOrderStats5mDTO>();

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns);

            JsonElement datas = data.GetProperty("data");

            for (int i = 0; i < datas.GetArrayLength(); i++)
            {
                SuperCandlesOrderStats5mDTO orderStatsDTO = new SuperCandlesOrderStats5mDTO()
                {
                    TradeDate = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[0].SourceIndex]),
                    TradeTime = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[2].SourceIndex]),

                    PutOrdersB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[3].SourceIndex]),
                    PutOrdersS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[4].SourceIndex]),
                    PutValB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[5].SourceIndex]),
                    PutValS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[6].SourceIndex]),
                    PutVolB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[7].SourceIndex]),
                    PutVolS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[8].SourceIndex]),
                    PutVwapB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[9].SourceIndex]),
                    PutVwapS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[10].SourceIndex]),
                    PutVol = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[11].SourceIndex]),
                    PutVal = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[12].SourceIndex]),
                    PutOrders = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[13].SourceIndex]),

                    CancelOrdersB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[14].SourceIndex]),
                    CancelOrdersS = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[15].SourceIndex]),
                    CancelValB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[16].SourceIndex]),
                    CancelValS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[17].SourceIndex]),
                    CancelVolB = ParseHelpers.GetIntOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[18].SourceIndex]),
                    CancelVolS = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[19].SourceIndex]),
                    CancelVwapB = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[20].SourceIndex]),
                    CancelVwapS = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[21].SourceIndex]),
                    CancelVol = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[22].SourceIndex]),
                    CancelVal = ParseHelpers.GetDoubleOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[23].SourceIndex]),
                    CancelOrders = ParseHelpers.GetLongOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[24].SourceIndex]),

                    SysTime = ParseHelpers.GetDateTimeOrNull(datas[i][ColumnAndNumbersForParsing.AlgOrderStats5mExpectedColumns[25].SourceIndex])
                };

                orderStatsList.Add(orderStatsDTO);
            }

            return orderStatsList;
        }

        public static PaginationCursorDTO ParseAlgCandlesDataCursor(JsonDocument jsonDocument)
        {
            

            JsonElement root = jsonDocument.RootElement;
            JsonElement data = root.GetProperty("data.cursor");
            JsonElement columns = data.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.AlgCandlesDataCursorExpectedColumns);


            JsonElement datas = data.GetProperty("data");
           

            PaginationCursorDTO paginationCursor = new PaginationCursorDTO()
            {
                Index = ParseHelpers.GetIntOrNull(datas[0][ColumnAndNumbersForParsing.AlgCandlesDataCursorExpectedColumns[0].SourceIndex]),
                Total = ParseHelpers.GetIntOrNull(datas[0][ColumnAndNumbersForParsing.AlgCandlesDataCursorExpectedColumns[1].SourceIndex]),
                PageSize = ParseHelpers.GetIntOrNull(datas[0][ColumnAndNumbersForParsing.AlgCandlesDataCursorExpectedColumns[2].SourceIndex])
            };
                
            

            return paginationCursor;

        }
        */
    }
}
