using ProtoBuf;
using ProtoBuf.Meta;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

/// <summary>Protobuf row matching moex_trade_stats_5m_stock column order.</summary>
[ProtoContract]
public struct TradeStatsStockRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public double? PrOpen { get; set; }
    [ProtoMember(4)] public double? PrHigh { get; set; }
    [ProtoMember(5)] public double? PrLow { get; set; }
    [ProtoMember(6)] public double? PrClose { get; set; }
    [ProtoMember(7)] public double? PrStd { get; set; }
    [ProtoMember(8)] public int? Vol { get; set; }
    [ProtoMember(9)] public double? Val { get; set; }
    [ProtoMember(10)] public int? Trades { get; set; }
    [ProtoMember(11)] public double? PrVwap { get; set; }
    [ProtoMember(12)] public double? PrChange { get; set; }
    [ProtoMember(13)] public int? TradesB { get; set; }
    [ProtoMember(14)] public int? TradesS { get; set; }
    [ProtoMember(15)] public double? ValB { get; set; }
    [ProtoMember(16)] public double? ValS { get; set; }
    [ProtoMember(17)] public long? VolB { get; set; }
    [ProtoMember(18)] public long? VolS { get; set; }
    [ProtoMember(19)] public double? Disb { get; set; }
    [ProtoMember(20)] public double? PrVwapB { get; set; }
    [ProtoMember(21)] public double? PrVwapS { get; set; }
    [ProtoMember(22)] public int? SecPrOpen { get; set; }
    [ProtoMember(23)] public int? SecPrHigh { get; set; }
    [ProtoMember(24)] public int? SecPrLow { get; set; }
    [ProtoMember(25)] public int? SecPrClose { get; set; }
    [ProtoMember(26)] public ulong? Systime { get; set; }
}

/// <summary>Protobuf row matching moex_trade_stats_5m_futures column order.</summary>
[ProtoContract]
public struct TradeStatsFuturesRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public string? AssetCode { get; set; }
    [ProtoMember(4)] public double? PrOpen { get; set; }
    [ProtoMember(5)] public double? PrHigh { get; set; }
    [ProtoMember(6)] public double? PrLow { get; set; }
    [ProtoMember(7)] public double? PrClose { get; set; }
    [ProtoMember(8)] public double? PrStd { get; set; }
    [ProtoMember(9)] public long? Vol { get; set; }
    [ProtoMember(10)] public long? Val { get; set; }
    [ProtoMember(11)] public int? Trades { get; set; }
    [ProtoMember(12)] public double? PrVwap { get; set; }
    [ProtoMember(13)] public double? PrChange { get; set; }
    [ProtoMember(14)] public int? TradesB { get; set; }
    [ProtoMember(15)] public int? TradesS { get; set; }
    [ProtoMember(16)] public double? ValB { get; set; }
    [ProtoMember(17)] public double? ValS { get; set; }
    [ProtoMember(18)] public long? VolB { get; set; }
    [ProtoMember(19)] public long? VolS { get; set; }
    [ProtoMember(20)] public double? Disb { get; set; }
    [ProtoMember(21)] public double? PrVwapB { get; set; }
    [ProtoMember(22)] public double? PrVwapS { get; set; }
    [ProtoMember(23)] public double? Im { get; set; }
    [ProtoMember(24)] public long? OiOpen { get; set; }
    [ProtoMember(25)] public long? OiHigh { get; set; }
    [ProtoMember(26)] public long? OiLow { get; set; }
    [ProtoMember(27)] public long? OiClose { get; set; }
    [ProtoMember(28)] public int? SecPrOpen { get; set; }
    [ProtoMember(29)] public int? SecPrHigh { get; set; }
    [ProtoMember(30)] public int? SecPrLow { get; set; }
    [ProtoMember(31)] public int? SecPrClose { get; set; }
    [ProtoMember(32)] public ulong? Systime { get; set; }
}

[ProtoSerializable(typeof(TradeStatsStockRow))]
[ProtoSerializable(typeof(TradeStatsFuturesRow))]
public partial class MarketProtobufModel : TypeModel
{
}
