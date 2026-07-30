using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Parsing;

namespace ProjectTraiding.Moex.Clients
{
    /// <summary>
    /// Полные паспорта девяти курсорных видов данных ALGOPACK. Каждый паспорт — структура
    /// без состояния и без экземпляров: она существует только как параметр типа обобщённых
    /// клиента и загрузочного обработчика.
    /// </summary>
    public readonly struct TradeStatsStockCursorKind : IAlgCursorKind<SuperCandlesTradeStats5mDTO>
    {
        public static string DataKind => "tradestats";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/tradestats/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.TradeStats;
        public static string TelemetryMarket => MoexMarkets.Stock;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.AlgCandlesTradeStatSchema;

        public static List<SuperCandlesTradeStats5mDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseTradeStatsStock(body, out cursor);
    }

    public readonly struct TradeStatsFuturesCursorKind : IAlgCursorKind<SuperCandlesFuturesTradeStats5mDTO>
    {
        public static string DataKind => "tradestats";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/tradestats/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.TradeStats;
        public static string TelemetryMarket => MoexMarkets.Futures;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.FuturesTradeStatsSchema;

        public static List<SuperCandlesFuturesTradeStats5mDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseTradeStatsFutures(body, out cursor);
    }

    public readonly struct ObStatsStockCursorKind : IAlgCursorKind<SuperCandlesOrderBookStats5mDTO>
    {
        public static string DataKind => "obstats";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/obstats/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.OBStats;
        public static string TelemetryMarket => MoexMarkets.Stock;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.AlgOrderBookStats5mSchema;

        public static List<SuperCandlesOrderBookStats5mDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseOBStatsStock(body, out cursor);
    }

    public readonly struct ObStatsFuturesCursorKind : IAlgCursorKind<SuperCandlesFuturesOrderBookStats5mDTO>
    {
        public static string DataKind => "obstats";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/obstats/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.OBStats;
        public static string TelemetryMarket => MoexMarkets.Futures;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.AlgFuturesOrderBookSchema;

        public static List<SuperCandlesFuturesOrderBookStats5mDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseOBStatsFutures(body, out cursor);
    }

    public readonly struct OrderStatsStockCursorKind : IAlgCursorKind<SuperCandlesOrderStats5mDTO>
    {
        public static string DataKind => "orderstats";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/orderstats/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.OrderStats;
        public static string TelemetryMarket => MoexMarkets.Stock;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.AlgOrderStats5mSchema;

        public static List<SuperCandlesOrderStats5mDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseOrderStatsStock(body, out cursor);
    }

    public readonly struct Hi2StockCursorKind : IAlgCursorKind<Hi2AssetDTO>
    {
        public static string DataKind => "hi2";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/hi2/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.Hi2;
        public static string TelemetryMarket => MoexMarkets.Stock;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.Hi2AssetSchema;

        public static List<Hi2AssetDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseHi2Stock(body, out cursor);
    }

    public readonly struct Hi2FuturesCursorKind : IAlgCursorKind<Hi2FuturesDTO>
    {
        public static string DataKind => "hi2";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/hi2/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.Hi2;
        public static string TelemetryMarket => MoexMarkets.Futures;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.Hi2FuturesSchema;

        public static List<Hi2FuturesDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseHi2Futures(body, out cursor);
    }

    public readonly struct MegaAlertsStockCursorKind : IAlgCursorKind<MegaAlertsAssetsDTO>
    {
        // Вид задачи и метка телеметрии намеренно различаются: столбец data_kind хранит
        // "mega_alerts", в телеметрию уходит "alerts".
        public static string DataKind => "mega_alerts";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/alerts/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.MegaAlerts;
        public static string TelemetryMarket => MoexMarkets.Stock;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.MegaAlertsAssetSchema;

        public static List<MegaAlertsAssetsDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseMegaAlertsStock(body, out cursor);
    }

    public readonly struct MegaAlertsFuturesCursorKind : IAlgCursorKind<MegaAlertsFuturesDTO>
    {
        // Вид задачи и метка телеметрии намеренно различаются: столбец data_kind хранит
        // "mega_alerts", в телеметрию уходит "alerts".
        public static string DataKind => "mega_alerts";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/alerts/{secid}.json";

        public static string TelemetryDataKind => MoexDataKinds.MegaAlerts;
        public static string TelemetryMarket => MoexMarkets.Futures;
        public static ColumnAndNumbersForParsing.ExpectedSchema Schema =>
            ColumnAndNumbersForParsing.MegaAlertsFuturesSchema;

        public static List<MegaAlertsFuturesDTO> Parse(
            ReadOnlySpan<byte> body, out PaginationCursorDTO cursor)
            => ParsingAlgUtf8.ParseMegaAlertsFutures(body, out cursor);
    }
}
