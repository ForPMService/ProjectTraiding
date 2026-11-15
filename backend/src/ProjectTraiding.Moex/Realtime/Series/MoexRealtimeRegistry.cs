using System.Text;
using ProjectTraiding.Moex.Series;

namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>
/// Declares MOEX realtime source columns and their HTTP columns parameters.
/// Candle source columns are shared with the historical series declarations.
/// </summary>
public static class MoexRealtimeRegistry
{
    public static readonly SourceColumn[] TradesStockSourceColumns =
    [
        new(0, "TRADENO"u8.ToArray()),
        new(1, "TRADETIME"u8.ToArray()),
        new(2, "SECID"u8.ToArray()),
        new(3, "PRICE"u8.ToArray()),
        new(4, "QUANTITY"u8.ToArray()),
        new(5, "PERIOD"u8.ToArray()),
        new(6, "VALUE"u8.ToArray()),
        new(7, "BOARDID"u8.ToArray()),
        new(8, "SYSTIME"u8.ToArray()),
        new(9, "BUYSELL"u8.ToArray()),
        new(10, "DECIMALS"u8.ToArray()),
        new(11, "TRADINGSESSION"u8.ToArray()),
        new(12, "TRADEDATE"u8.ToArray()),
        new(13, "TRADE_SESSION_DATE"u8.ToArray()),
    ];

    public static readonly SourceColumn[] TradesFuturesSourceColumns =
    [
        new(0, "TRADENO"u8.ToArray()),
        new(1, "BOARDNAME"u8.ToArray()),
        new(2, "SECID"u8.ToArray()),
        new(3, "TRADEDATE"u8.ToArray()),
        new(4, "TRADETIME"u8.ToArray()),
        new(5, "PRICE"u8.ToArray()),
        new(6, "QUANTITY"u8.ToArray()),
        new(7, "SYSTIME"u8.ToArray()),
        new(8, "RECNO"u8.ToArray()),
        new(9, "OPENPOSITION"u8.ToArray()),
        new(10, "OFFMARKETDEAL"u8.ToArray()),
        new(11, "BUYSELL"u8.ToArray()),
        new(12, "TRADE_SESSION_DATE"u8.ToArray()),
    ];

    public static readonly SourceColumn[] OrderbookSourceColumns =
    [
        new(0, "BOARDID"u8.ToArray()),
        new(1, "SECID"u8.ToArray()),
        new(2, "BUYSELL"u8.ToArray()),
        new(3, "PRICE"u8.ToArray()),
        new(4, "QUANTITY"u8.ToArray()),
        new(5, "SEQNUM"u8.ToArray()),
        new(6, "UPDATETIME"u8.ToArray()),
        new(7, "DECIMALS"u8.ToArray()),
    ];

    public static readonly SourceColumn[] CandleSourceColumns =
        MoexSeriesSpec.CandleSourceColumns;

    public static readonly string TradesStockColumnsParam =
        BuildColumnsParam(TradesStockSourceColumns);
    public static readonly string TradesFuturesColumnsParam =
        BuildColumnsParam(TradesFuturesSourceColumns);
    public static readonly string OrderbookColumnsParam =
        BuildColumnsParam(OrderbookSourceColumns);
    public static readonly string CandlesColumnsParam =
        BuildColumnsParam(CandleSourceColumns);

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

    private static string BuildColumnsParam(SourceColumn[] columns)
    {
        string[] names = new string[columns.Length];
        for (int i = 0; i < columns.Length; i++)
            names[i] = Encoding.UTF8.GetString(columns[i].Name);
        return string.Join(',', names);
    }
}
