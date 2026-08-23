using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;

namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>
/// Реестр деклараций приёма. Каждая декларация одновременно задаёт форму запроса,
/// проверку ответа и строку вставки, чтобы у горячего тракта не было второй карты колонок.
/// </summary>
public static class MoexRealtimeRegistry
{
    public static readonly MoexRealtimeSpec TradesStock = new(
        rootKey: "trades",
        sourceColumns:
        [
            new(0, "TRADENO"u8.ToArray(), ColumnKind.Int64),
            new(1, "TRADETIME"u8.ToArray(), ColumnKind.MomentTime),
            new(2, "SECID"u8.ToArray(), ColumnKind.String),
            new(3, "PRICE"u8.ToArray(), ColumnKind.Double),
            new(4, "QUANTITY"u8.ToArray(), ColumnKind.Int64),
            new(5, "PERIOD"u8.ToArray(), ColumnKind.String),
            new(6, "VALUE"u8.ToArray(), ColumnKind.Double),
            new(7, "BOARDID"u8.ToArray(), ColumnKind.String),
            // Разбор по байтам вместо строки: временной строки на каждую сделку больше
            // не возникает. Непригодное значение даёт пустую ячейку, а не отказ страницы —
            // решение принято осознанно: поле необязательное, а отказ уничтожал всю пачку.
            new(8, "SYSTIME"u8.ToArray(), ColumnKind.DateTime),
            new(9, "BUYSELL"u8.ToArray(), ColumnKind.String),
            new(10, "DECIMALS"u8.ToArray(), ColumnKind.Int32),
            new(11, "TRADINGSESSION"u8.ToArray(), ColumnKind.String),
            new(12, "TRADEDATE"u8.ToArray(), ColumnKind.MomentDate),
            new(13, "TRADE_SESSION_DATE"u8.ToArray(), ColumnKind.String),
        ],
        table: "moex_trades_stock",
        targetColumns:
        [
            new("secid", "LowCardinality(String)", FillRule.TaskSecId),
            new("source_time", "DateTime64(3, 'Europe/Moscow')", FillRule.SourceDateTime,
                12, 1, Required: true),
            new("trade_no", "Int64", FillRule.Direct, 0, Required: true,
                RequiredMessage: "Сделка отвергнута: TRADENO пуст."),
            new("board_id", "LowCardinality(Nullable(String))", FillRule.Direct, 7),
            new("price", "Nullable(Float64)", FillRule.Direct, 3),
            new("quantity", "Nullable(Int64)", FillRule.Direct, 4),
            new("value", "Nullable(Float64)", FillRule.Direct, 6),
            new("period", "LowCardinality(Nullable(String))", FillRule.Direct, 5),
            new("buy_sell", "LowCardinality(Nullable(String))", FillRule.Direct, 9),
            new("decimals", "Nullable(Int32)", FillRule.Direct, 10),
            new("trading_session", "LowCardinality(Nullable(String))", FillRule.Direct, 11),
            new("trade_session_date", "Nullable(Date)", FillRule.SessionDate),
            new("systime", "Nullable(DateTime64(3, 'Europe/Moscow'))", FillRule.WallClock, 8),
            new("ingest_priority", "UInt8", FillRule.Constant, Constant: (byte)0),
        ],
        tokenPrefix: "trades:tick:stock",
        emptySecidMessage: "Запись сделок отвергнута: secid пустой.",
        keyTimeIndex: 1,
        tradeNoIndex: 2);

    public static readonly MoexRealtimeSpec TradesFutures = new(
        rootKey: "trades",
        sourceColumns:
        [
            new(0, "TRADENO"u8.ToArray(), ColumnKind.Int64),
            new(1, "BOARDNAME"u8.ToArray(), ColumnKind.String),
            new(2, "SECID"u8.ToArray(), ColumnKind.String),
            new(3, "TRADEDATE"u8.ToArray(), ColumnKind.MomentDate),
            new(4, "TRADETIME"u8.ToArray(), ColumnKind.MomentTime),
            new(5, "PRICE"u8.ToArray(), ColumnKind.Double),
            new(6, "QUANTITY"u8.ToArray(), ColumnKind.Int64),
            // Разбор по байтам вместо строки: временной строки на каждую сделку больше
            // не возникает. Непригодное значение даёт пустую ячейку, а не отказ страницы —
            // решение принято осознанно: поле необязательное, а отказ уничтожал всю пачку.
            new(7, "SYSTIME"u8.ToArray(), ColumnKind.DateTime),
            new(8, "RECNO"u8.ToArray(), ColumnKind.Int64),
            new(9, "OPENPOSITION"u8.ToArray(), ColumnKind.Int64),
            new(10, "OFFMARKETDEAL"u8.ToArray(), ColumnKind.Int32),
            new(11, "BUYSELL"u8.ToArray(), ColumnKind.String),
            new(12, "TRADE_SESSION_DATE"u8.ToArray(), ColumnKind.String),
        ],
        table: "moex_trades_futures",
        targetColumns:
        [
            new("secid", "LowCardinality(String)", FillRule.TaskSecId),
            new("source_time", "DateTime64(3, 'Europe/Moscow')", FillRule.SourceDateTime,
                3, 4, Required: true),
            new("trade_no", "Int64", FillRule.Direct, 0, Required: true,
                RequiredMessage: "Сделка отвергнута: TRADENO пуст."),
            new("board_name", "LowCardinality(Nullable(String))", FillRule.Direct, 1),
            new("price", "Nullable(Float64)", FillRule.Direct, 5),
            new("quantity", "Nullable(Int64)", FillRule.Direct, 6),
            new("rec_no", "Nullable(Int64)", FillRule.Direct, 8),
            new("open_position", "Nullable(Int64)", FillRule.Direct, 9),
            new("off_market_deal", "Nullable(Int32)", FillRule.Direct, 10),
            new("buy_sell", "LowCardinality(Nullable(String))", FillRule.Direct, 11),
            new("trade_session_date", "Nullable(Date)", FillRule.SessionDate),
            new("systime", "Nullable(DateTime64(3, 'Europe/Moscow'))", FillRule.WallClock, 7),
            new("ingest_priority", "UInt8", FillRule.Constant, Constant: (byte)0),
        ],
        tokenPrefix: "trades:tick:futures",
        emptySecidMessage: "Запись сделок отвергнута: secid пустой.",
        keyTimeIndex: 1,
        tradeNoIndex: 2);

    public static readonly MoexRealtimeSpec Orderbook = new(
        rootKey: "orderbook",
        sourceColumns:
        [
            new(0, "BOARDID"u8.ToArray(), ColumnKind.String),
            new(1, "SECID"u8.ToArray(), ColumnKind.String),
            new(2, "BUYSELL"u8.ToArray(), ColumnKind.String),
            new(3, "PRICE"u8.ToArray(), ColumnKind.Double),
            new(4, "QUANTITY"u8.ToArray(), ColumnKind.Int64),
            new(5, "SEQNUM"u8.ToArray(), ColumnKind.Int64),
            new(6, "UPDATETIME"u8.ToArray(), ColumnKind.String),
            new(7, "DECIMALS"u8.ToArray(), ColumnKind.Int64),
        ],
        table: "moex_orderbook",
        targetColumns:
        [
            new("secid", "LowCardinality(String)", FillRule.TaskSecId),
            new("source_time", "DateTime64(3, 'Europe/Moscow')", FillRule.SnapshotTimeFromSeqNum,
                5, Required: true),
            new("buy_sell", "LowCardinality(String)", FillRule.Direct, 2, Required: true,
                RequiredMessage: "Строка стакана отвергнута: BUYSELL пуст."),
            new("price", "Float64", FillRule.Direct, 3, Required: true,
                RequiredMessage: "Строка стакана отвергнута: PRICE пуст."),
            new("board_id", "LowCardinality(Nullable(String))", FillRule.Direct, 0),
            new("quantity", "Nullable(Int64)", FillRule.Direct, 4),
            new("decimals", "Nullable(Int64)", FillRule.Direct, 7),
            new("trade_session_date", "Nullable(Date)", FillRule.SessionDate),
            new("ingest_priority", "UInt8", FillRule.Constant, Constant: (byte)0),
        ],
        tokenPrefix: "orderbook:snapshot",
        emptySecidMessage: "Запись стакана отвергнута: secid пустой.",
        keyTimeIndex: 1,
        tradeNoIndex: -1);

    public static readonly MoexRealtimeSpec Candles1m = new(
        rootKey: "candles",
        sourceColumns:
        [
            new(0, "open"u8.ToArray(), ColumnKind.Double),
            new(1, "close"u8.ToArray(), ColumnKind.Double),
            new(2, "high"u8.ToArray(), ColumnKind.Double),
            new(3, "low"u8.ToArray(), ColumnKind.Double),
            new(4, "value"u8.ToArray(), ColumnKind.Double),
            new(5, "volume"u8.ToArray(), ColumnKind.Double),
            new(6, "begin"u8.ToArray(), ColumnKind.DateTime),
            new(7, "end"u8.ToArray(), ColumnKind.DateTime),
        ],
        table: "moex_candles_1m",
        targetColumns:
        [
            new("secid", "LowCardinality(String)", FillRule.TaskSecId),
            new("open", "Nullable(Float64)", FillRule.Direct, 0),
            new("close", "Nullable(Float64)", FillRule.Direct, 1),
            new("high", "Nullable(Float64)", FillRule.Direct, 2),
            new("low", "Nullable(Float64)", FillRule.Direct, 3),
            new("value", "Nullable(Float64)", FillRule.Direct, 4),
            new("volume", "Nullable(Float64)", FillRule.Direct, 5),
            new("begin", "DateTime64(3, 'Europe/Moscow')", FillRule.WallClock, 6),
            new("end", "Nullable(DateTime64(3, 'Europe/Moscow'))", FillRule.WallClock, 7),
            new("ingest_priority", "UInt8", FillRule.Constant, Constant: (byte)0),
        ],
        tokenPrefix: "candles:1m",
        emptySecidMessage: "Загрузка свечей отвергнута: secid пустой.",
        keyTimeIndex: 7,
        tradeNoIndex: -1);

    /// <summary>
    /// Декларация сделок по рынку. Резольвер общий для клиента и службы: если бы декларацию
    /// выбирали в двух местах по-разному, стало бы выразимым сочетание «рынок акций и
    /// декларация фьючерсов», которого сегодня в системе быть не может.
    /// </summary>
    public static MoexRealtimeSpec TradesFor(string market)
    {
        if (market == MoexMarkets.Stock)
            return TradesStock;
        if (market == MoexMarkets.Futures)
            return TradesFutures;

        throw new InvalidOperationException(
            $"Неизвестный рынок инструмента приёмника: '{market}'.");
    }

    /// <summary>Декларация свечей по коду интервала. Приём пока ведёт только минутные.</summary>
    public static MoexRealtimeSpec CandlesFor(int interval)
    {
        if (interval == 1)
            return Candles1m;

        throw new InvalidOperationException(
            $"Приём свечей не объявлен для интервала {interval}.");
    }
}
