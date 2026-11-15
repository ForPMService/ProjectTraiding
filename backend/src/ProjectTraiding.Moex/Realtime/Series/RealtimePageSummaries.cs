using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Realtime.Series;

public readonly record struct RealtimeRowFailure(int Position, string Reason);

/// <summary>One trades response; its concrete rows are retained only until validation and insert.</summary>
public sealed class RealtimeStockTradesPage
{
    public required List<RealtimeStockTradeRow> Rows { get; init; }
    public required List<RealtimeRowFailure> RowFailures { get; init; }
    public int SourceRowsCount { get; init; }
    public DateTime LastTime { get; init; }
    public long? LastTradeNo { get; init; }
    public long? MaxTradeNo { get; init; }
    public DateTime? MaxTradeNoTime { get; init; }
    public Exception? MaxTradeNoTimeFailure { get; init; }
}

/// <summary>One futures trades response; its concrete rows are retained only until validation and insert.</summary>
public sealed class RealtimeFuturesTradesPage
{
    public required List<RealtimeFuturesTradeRow> Rows { get; init; }
    public required List<RealtimeRowFailure> RowFailures { get; init; }
    public int SourceRowsCount { get; init; }
    public DateTime LastTime { get; init; }
    public long? LastTradeNo { get; init; }
    public long? MaxTradeNo { get; init; }
    public DateTime? MaxTradeNoTime { get; init; }
    public Exception? MaxTradeNoTimeFailure { get; init; }
}

/// <summary>One complete orderbook response. Rows are held only while validating the snapshot.</summary>
public sealed class RealtimeOrderbookPage
{
    public required List<RealtimeOrderbookRow> Rows { get; init; }
    public required List<RealtimeRowFailure> RowFailures { get; init; }
    public int SourceRowsCount { get; init; }
    public DateTime FirstTime { get; init; }
}

/// <summary>Compact result for one candle HTTP page after direct append to its writer.</summary>
public sealed class RealtimeCandlesPageSummary
{
    public required List<RealtimeRowFailure> RowFailures { get; init; }
    public List<CandleRow>? AcceptedCandles { get; init; }
    public int SourceRowsCount { get; init; }
    public int AcceptedRowsCount { get; init; }
    public DateTime? MaxAcceptedBegin { get; init; }
}
