using System.Text;
using ProjectTraiding.Moex.Series;

namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>
/// Sole declaration of MOEX realtime source columns and the corresponding HTTP columns
/// parameters. Target row layouts are represented by their Protobuf structs.
/// </summary>
public static class MoexRealtimeRegistry
{
    public static readonly SourceColumn[] TradesStockSourceColumns =
    [
        new(0, "TRADENO"u8.ToArray(), ColumnKind.Int64),
        new(1, "TRADETIME"u8.ToArray(), ColumnKind.MomentTime),
        new(2, "SECID"u8.ToArray(), ColumnKind.String),
        new(3, "PRICE"u8.ToArray(), ColumnKind.Double),
        new(4, "QUANTITY"u8.ToArray(), ColumnKind.Int64),
        new(5, "PERIOD"u8.ToArray(), ColumnKind.String),
        new(6, "VALUE"u8.ToArray(), ColumnKind.Double),
        new(7, "BOARDID"u8.ToArray(), ColumnKind.String),
        new(8, "SYSTIME"u8.ToArray(), ColumnKind.DateTime),
        new(9, "BUYSELL"u8.ToArray(), ColumnKind.String),
        new(10, "DECIMALS"u8.ToArray(), ColumnKind.Int32),
        new(11, "TRADINGSESSION"u8.ToArray(), ColumnKind.String),
        new(12, "TRADEDATE"u8.ToArray(), ColumnKind.MomentDate),
        new(13, "TRADE_SESSION_DATE"u8.ToArray(), ColumnKind.String),
    ];

    public static readonly SourceColumn[] TradesFuturesSourceColumns =
    [
        new(0, "TRADENO"u8.ToArray(), ColumnKind.Int64),
        new(1, "BOARDNAME"u8.ToArray(), ColumnKind.String),
        new(2, "SECID"u8.ToArray(), ColumnKind.String),
        new(3, "TRADEDATE"u8.ToArray(), ColumnKind.MomentDate),
        new(4, "TRADETIME"u8.ToArray(), ColumnKind.MomentTime),
        new(5, "PRICE"u8.ToArray(), ColumnKind.Double),
        new(6, "QUANTITY"u8.ToArray(), ColumnKind.Int64),
        new(7, "SYSTIME"u8.ToArray(), ColumnKind.DateTime),
        new(8, "RECNO"u8.ToArray(), ColumnKind.Int64),
        new(9, "OPENPOSITION"u8.ToArray(), ColumnKind.Int64),
        new(10, "OFFMARKETDEAL"u8.ToArray(), ColumnKind.Int32),
        new(11, "BUYSELL"u8.ToArray(), ColumnKind.String),
        new(12, "TRADE_SESSION_DATE"u8.ToArray(), ColumnKind.String),
    ];

    public static readonly SourceColumn[] OrderbookSourceColumns =
    [
        new(0, "BOARDID"u8.ToArray(), ColumnKind.String),
        new(1, "SECID"u8.ToArray(), ColumnKind.String),
        new(2, "BUYSELL"u8.ToArray(), ColumnKind.String),
        new(3, "PRICE"u8.ToArray(), ColumnKind.Double),
        new(4, "QUANTITY"u8.ToArray(), ColumnKind.Int64),
        new(5, "SEQNUM"u8.ToArray(), ColumnKind.Int64),
        new(6, "UPDATETIME"u8.ToArray(), ColumnKind.String),
        new(7, "DECIMALS"u8.ToArray(), ColumnKind.Int64),
    ];

    public static readonly SourceColumn[] CandleSourceColumns =
    [
        new(0, "open"u8.ToArray(), ColumnKind.Double),
        new(1, "close"u8.ToArray(), ColumnKind.Double),
        new(2, "high"u8.ToArray(), ColumnKind.Double),
        new(3, "low"u8.ToArray(), ColumnKind.Double),
        new(4, "value"u8.ToArray(), ColumnKind.Double),
        new(5, "volume"u8.ToArray(), ColumnKind.Double),
        new(6, "begin"u8.ToArray(), ColumnKind.DateTime),
        new(7, "end"u8.ToArray(), ColumnKind.DateTime),
    ];

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

    public static readonly string[] CandleInsertColumns =
    [
        "secid", "open", "close", "high", "low", "value", "volume", "begin", "end",
        "ingest_priority",
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
