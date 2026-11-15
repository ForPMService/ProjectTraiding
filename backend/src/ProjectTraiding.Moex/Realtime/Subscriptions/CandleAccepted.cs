using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Realtime.Subscriptions;

/// <summary>A validated candle update accepted from a live MOEX source.</summary>
public sealed record CandleAccepted(CandleRow Candle, string Market, int Interval);
