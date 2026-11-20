using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

public static class MoexSeriesRegistry
{
    private static readonly string[] TradeStatsStockInsertColumns =
    [
        "secid", "source_time", "pr_open", "pr_high",
        "pr_low", "pr_close", "pr_std", "vol",
        "val", "trades", "pr_vwap", "pr_change",
        "trades_b", "trades_s", "val_b", "val_s",
        "vol_b", "vol_s", "disb", "pr_vwap_b",
        "pr_vwap_s", "sec_pr_open", "sec_pr_high", "sec_pr_low",
        "sec_pr_close", "systime",
    ];

    private static readonly string[] TradeStatsFuturesInsertColumns =
    [
        "secid", "source_time", "asset_code", "pr_open",
        "pr_high", "pr_low", "pr_close", "pr_std",
        "vol", "val", "trades", "pr_vwap",
        "pr_change", "trades_b", "trades_s", "val_b",
        "val_s", "vol_b", "vol_s", "disb",
        "pr_vwap_b", "pr_vwap_s", "im", "oi_open",
        "oi_high", "oi_low", "oi_close", "sec_pr_open",
        "sec_pr_high", "sec_pr_low", "sec_pr_close", "systime",
    ];

    private static readonly string[] ObStatsStockInsertColumns =
    [
        "secid", "source_time", "spread_bbo", "spread_lv10",
        "spread_1mio", "levels_b", "levels_s", "vol_b",
        "vol_s", "val_b", "val_s", "imbalance_vol_bbo",
        "imbalance_val_bbo", "imbalance_vol", "imbalance_val", "vwap_b",
        "vwap_s", "vwap_b_1mio", "vwap_s_1mio", "systime",
    ];

    private static readonly string[] ObStatsFuturesInsertColumns =
    [
        "secid", "source_time", "asset_code", "mid_price",
        "micro_price", "spread_l1", "spread_l2", "spread_l3",
        "spread_l5", "spread_l10", "spread_l20", "levels_b",
        "levels_s", "vol_b_l1", "vol_b_l2", "vol_b_l3",
        "vol_b_l5", "vol_b_l10", "vol_b_l20", "vol_s_l1",
        "vol_s_l2", "vol_s_l3", "vol_s_l5", "vol_s_l10",
        "vol_s_l20", "vwap_b_l3", "vwap_b_l5", "vwap_b_l10",
        "vwap_b_l20", "vwap_s_l3", "vwap_s_l5", "vwap_s_l10",
        "vwap_s_l20", "systime",
    ];

    private static readonly string[] OrderStatsStockInsertColumns =
    [
        "secid", "source_time", "put_orders_b", "put_orders_s",
        "put_val_b", "put_val_s", "put_vol_b", "put_vol_s",
        "put_vwap_b", "put_vwap_s", "put_vol", "put_val",
        "put_orders", "cancel_orders_b", "cancel_orders_s", "cancel_val_b",
        "cancel_val_s", "cancel_vol_b", "cancel_vol_s", "cancel_vwap_b",
        "cancel_vwap_s", "cancel_vol", "cancel_val", "cancel_orders",
        "systime",
    ];

    private static readonly string[] Hi2StockInsertColumns =
    [
        "secid", "source_time", "metric", "value",
        "reference", "systime",
    ];

    private static readonly string[] Hi2FuturesInsertColumns =
    [
        "secid", "source_time", "asset_code", "metric",
        "value", "reference", "systime",
    ];

    private static readonly string[] MegaAlertsStockInsertColumns =
    [
        "secid", "source_time", "alert_type", "threshold",
        "value", "reference", "systime",
    ];

    private static readonly string[] MegaAlertsFuturesInsertColumns =
    [
        "secid", "source_time", "asset_code", "alert_type",
        "threshold", "value", "reference", "systime",
    ];

    private static readonly string[] FutoiInsertColumns =
    [
        "secid", "source_time", "clgroup", "sess_id",
        "seqnum", "pos", "pos_long", "pos_short",
        "pos_long_num", "pos_short_num", "trade_session_date", "systime",
    ];
    public static readonly MoexSeriesSpec TradeStatsStock = new()
    {
        DataKind = "tradestats",
        Market = "stock",
        MethodTemplate = "/datashop/algopack/eq/tradestats/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.TradeStatsStock,
        Table = "moex_trade_stats_5m_stock",
        Columns = TradeStatsStockInsertColumns,
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
        Columns = TradeStatsFuturesInsertColumns,
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
        Columns = ObStatsStockInsertColumns,
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
        Columns = ObStatsFuturesInsertColumns,
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
        Columns = OrderStatsStockInsertColumns,
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
        Columns = Hi2StockInsertColumns,
        Pagination = PaginationKind.Cursor,
        PreserveCursorTimeGroup = true,
    };

    public static readonly MoexSeriesSpec Hi2Futures = new()
    {
        DataKind = "hi2",
        Market = "futures",
        MethodTemplate = "/datashop/algopack/fo/hi2/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.Hi2Futures,
        Table = "moex_hi2_daily_futures",
        Columns = Hi2FuturesInsertColumns,
        Pagination = PaginationKind.Cursor,
        PreserveCursorTimeGroup = true,
    };

    public static readonly MoexSeriesSpec MegaAlertsStock = new()
    {
        DataKind = "mega_alerts",
        Market = "stock",
        MethodTemplate = "/datashop/algopack/eq/alerts/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.MegaAlertsStock,
        Table = "moex_megaalerts_stock",
        Columns = MegaAlertsStockInsertColumns,
        Pagination = PaginationKind.Cursor,
        PreserveCursorTimeGroup = true,
    };

    public static readonly MoexSeriesSpec MegaAlertsFutures = new()
    {
        DataKind = "mega_alerts",
        Market = "futures",
        MethodTemplate = "/datashop/algopack/fo/alerts/{0}.json",
        RootKey = "data",
        InputColumns = MoexColumnSets.MegaAlertsFutures,
        Table = "moex_megaalerts_futures",
        Columns = MegaAlertsFuturesInsertColumns,
        Pagination = PaginationKind.Cursor,
        PreserveCursorTimeGroup = true,
    };

    public static readonly MoexSeriesSpec Futoi = new()
    {
        DataKind = "futoi",
        Market = "futures",
        MethodTemplate = "/analyticalproducts/futoi/securities/{0}.json",
        RootKey = "futoi",
        InputColumns = MoexColumnSets.Futoi,
        Table = "moex_futoi_5m",
        Columns = FutoiInsertColumns,
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
        Candles("stock", CandlesStockTemplate, 1, "moex_candles_1m");

    public static readonly MoexSeriesSpec CandlesStock10m =
        Candles("stock", CandlesStockTemplate, 10, "moex_candles_10m");

    public static readonly MoexSeriesSpec CandlesStock1h =
        Candles("stock", CandlesStockTemplate, 60, "moex_candles_1h");

    public static readonly MoexSeriesSpec CandlesStock1d =
        Candles("stock", CandlesStockTemplate, 24, "moex_candles_1d");

    public static readonly MoexSeriesSpec CandlesFutures1m =
        Candles("futures", CandlesFuturesTemplate, 1, "moex_candles_1m");

    public static readonly MoexSeriesSpec CandlesFutures10m =
        Candles("futures", CandlesFuturesTemplate, 10, "moex_candles_10m");

    public static readonly MoexSeriesSpec CandlesFutures1h =
        Candles("futures", CandlesFuturesTemplate, 60, "moex_candles_1h");

    public static readonly MoexSeriesSpec CandlesFutures1d =
        Candles("futures", CandlesFuturesTemplate, 24, "moex_candles_1d");
}
