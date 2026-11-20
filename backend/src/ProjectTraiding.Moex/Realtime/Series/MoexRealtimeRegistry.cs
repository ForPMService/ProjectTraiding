namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>
/// Declares realtime table destinations and insert columns.
/// </summary>
public static class MoexRealtimeRegistry
{
    public static readonly string[] StockTradeInsertColumns =
    [
        "secid", "source_time", "trade_no", "board_id", "price", "quantity", "value",
        "period", "buy_sell", "decimals", "trading_session", "trade_session_date",
        "systime", "ingest_priority",
    ];

    public static readonly string[] FuturesTradeInsertColumns =
    [
        "secid", "source_time", "trade_no", "board_name", "price", "quantity", "rec_no",
        "open_position", "off_market_deal", "buy_sell", "trade_session_date", "systime",
        "ingest_priority",
    ];

    public static readonly string[] OrderbookInsertColumns =
    [
        "secid", "source_time", "buy_sell", "price", "board_id", "quantity", "decimals",
        "trade_session_date", "ingest_priority",
    ];

    public const string StockTradesTable = "moex_trades_stock";
    public const string FuturesTradesTable = "moex_trades_futures";
    public const string OrderbookTable = "moex_orderbook";

    public static string CandleTableFor(int interval)
    {
        if (interval == 1)
            return "moex_candles_1m";
        if (interval == 10)
            return "moex_candles_10m";
        if (interval == 60)
            return "moex_candles_1h";
        if (interval == 24)
            return "moex_candles_1d";

        throw new InvalidOperationException(
            $"Приём свечей не объявлен для интервала {interval}.");
    }
}
