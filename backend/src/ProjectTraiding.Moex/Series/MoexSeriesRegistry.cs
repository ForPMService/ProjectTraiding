using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

public static class MoexSeriesRegistry
{
    private const string Candles1mTable = "moex_candles_1m";
    private const string Candles10mTable = "moex_candles_10m";
    private const string Candles1hTable = "moex_candles_1h";
    private const string Candles1dTable = "moex_candles_1d";

    public const string TradeStatsCandleTable = "moex_candles_5m";
    public const string RealtimeStockTradesTable = "moex_trades_stock";
    public const string RealtimeFuturesTradesTable = "moex_trades_futures";
    public const string RealtimeOrderbookTable = "moex_orderbook";

    public static readonly MoexSeriesSpec TradeStatsStock = new()
    {
        DataKind = "tradestats",
        Market = "stock",
        MethodTemplate = "/datashop/algopack/eq/tradestats/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.TradeStatsStock,
        Table = "moex_trade_stats_5m_stock",
        Columns = TradeStatsStockRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec TradeStatsFutures = new()
    {
        DataKind = "tradestats",
        Market = "futures",
        MethodTemplate = "/datashop/algopack/fo/tradestats/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.TradeStatsFutures,
        Table = "moex_trade_stats_5m_futures",
        Columns = TradeStatsFuturesRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec ObStatsStock = new()
    {
        DataKind = "obstats",
        Market = "stock",
        MethodTemplate = "/datashop/algopack/eq/obstats/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.ObStatsStock,
        Table = "moex_ob_stats_5m_stock",
        Columns = ObStatsStockRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec ObStatsFutures = new()
    {
        DataKind = "obstats",
        Market = "futures",
        MethodTemplate = "/datashop/algopack/fo/obstats/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.ObStatsFutures,
        Table = "moex_ob_stats_5m_futures",
        Columns = ObStatsFuturesRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec OrderStatsStock = new()
    {
        DataKind = "orderstats",
        Market = "stock",
        MethodTemplate = "/datashop/algopack/eq/orderstats/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.OrderStatsStock,
        Table = "moex_order_stats_5m_stock",
        Columns = OrderStatsStockRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec Hi2Stock = new()
    {
        DataKind = "hi2",
        Market = "stock",
        MethodTemplate = "/datashop/algopack/eq/hi2/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.Hi2Stock,
        Table = "moex_hi2_daily_stock",
        Columns = Hi2StockRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec Hi2Futures = new()
    {
        DataKind = "hi2",
        Market = "futures",
        MethodTemplate = "/datashop/algopack/fo/hi2/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.Hi2Futures,
        Table = "moex_hi2_daily_futures",
        Columns = Hi2FuturesRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec MegaAlertsStock = new()
    {
        DataKind = "mega_alerts",
        Market = "stock",
        MethodTemplate = "/datashop/algopack/eq/alerts/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.MegaAlertsStock,
        Table = "moex_megaalerts_stock",
        Columns = MegaAlertsStockRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec MegaAlertsFutures = new()
    {
        DataKind = "mega_alerts",
        Market = "futures",
        MethodTemplate = "/datashop/algopack/fo/alerts/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.MegaAlertsFutures,
        Table = "moex_megaalerts_futures",
        Columns = MegaAlertsFuturesRow.InsertColumns,
        Pagination = PaginationKind.Cursor,
    };

    public static readonly MoexSeriesSpec Futoi = new()
    {
        DataKind = "futoi",
        Market = "futures",
        MethodTemplate = "/analyticalproducts/futoi/securities/{0}.json",
        RootKey = "futoi",
        InputColumns = MoexColumnSets.Futoi,
        Table = "moex_futoi_5m",
        Columns = FutoiRow.InsertColumns,
        Pagination = PaginationKind.DaySplit,
    };

    // ═══════════════════════════════════════════════════════════
    // Свечи. Форма одна на оба рынка и все интервалы; различаются
    // шаблон адреса (рынок) и таблица (интервал).
    // Адрес двухместный: {0} — код инструмента, {1} — доска из задачи;
    // регистр доски (строчные у акций, прописные у фьючерсов) применяет
    // загрузчик по рынку декларации. Приоритет вставки 1 — исторический
    // тракт, перекрывающий строки приёмника (у него 0) при слиянии
    // ReplacingMergeTree.
    // ═══════════════════════════════════════════════════════════

    private static MoexSeriesSpec Candles(
        string market, string methodTemplate, int interval, string table) => new()
    {
        DataKind = "candles",
        Market = market,
        MethodTemplate = methodTemplate,
        RootKey = "candles",
        InputColumns = MoexColumnSets.Candles,
        Table = table,
        Columns = CandleRow.InsertColumns,
        Pagination = PaginationKind.FixedPage,
        CandleInterval = interval,
    };

    private const string CandlesStockTemplate =
        "/engines/stock/markets/shares/boards/{1}/securities/{0}/candles.json";

    private const string CandlesFuturesTemplate =
        "/engines/futures/markets/forts/boards/{1}/securities/{0}/candles.json";

    public static readonly MoexSeriesSpec CandlesStock1m =
        Candles("stock", CandlesStockTemplate, 1, Candles1mTable);

    public static readonly MoexSeriesSpec CandlesStock10m =
        Candles("stock", CandlesStockTemplate, 10, Candles10mTable);

    public static readonly MoexSeriesSpec CandlesStock1h =
        Candles("stock", CandlesStockTemplate, 60, Candles1hTable);

    public static readonly MoexSeriesSpec CandlesStock1d =
        Candles("stock", CandlesStockTemplate, 24, Candles1dTable);

    public static readonly MoexSeriesSpec CandlesFutures1m =
        Candles("futures", CandlesFuturesTemplate, 1, Candles1mTable);

    public static readonly MoexSeriesSpec CandlesFutures10m =
        Candles("futures", CandlesFuturesTemplate, 10, Candles10mTable);

    public static readonly MoexSeriesSpec CandlesFutures1h =
        Candles("futures", CandlesFuturesTemplate, 60, Candles1hTable);

    public static readonly MoexSeriesSpec CandlesFutures1d =
        Candles("futures", CandlesFuturesTemplate, 24, Candles1dTable);

    public static string CandleTableFor(int interval)
    {
        if (interval == 1)
            return Candles1mTable;
        if (interval == 10)
            return Candles10mTable;
        if (interval == 60)
            return Candles1hTable;
        if (interval == 24)
            return Candles1dTable;

        throw new InvalidOperationException(
            $"Приём свечей не объявлен для интервала {interval}.");
    }
}
