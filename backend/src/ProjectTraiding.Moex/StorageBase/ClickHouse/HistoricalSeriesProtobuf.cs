using ProtoBuf;
using ProtoBuf.Meta;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

[ProtoContract]
public struct ObStatsStockRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public double? SpreadBbo { get; set; }
    [ProtoMember(4)] public double? SpreadLv10 { get; set; }
    [ProtoMember(5)] public double? Spread1Mio { get; set; }
    [ProtoMember(6)] public int? LevelsB { get; set; }
    [ProtoMember(7)] public int? LevelsS { get; set; }
    [ProtoMember(8)] public long? VolB { get; set; }
    [ProtoMember(9)] public long? VolS { get; set; }
    [ProtoMember(10)] public long? ValB { get; set; }
    [ProtoMember(11)] public long? ValS { get; set; }
    [ProtoMember(12)] public double? ImbalanceVolBbo { get; set; }
    [ProtoMember(13)] public double? ImbalanceValBbo { get; set; }
    [ProtoMember(14)] public double? ImbalanceVol { get; set; }
    [ProtoMember(15)] public double? ImbalanceVal { get; set; }
    [ProtoMember(16)] public double? VwapB { get; set; }
    [ProtoMember(17)] public double? VwapS { get; set; }
    [ProtoMember(18)] public double? VwapB1Mio { get; set; }
    [ProtoMember(19)] public double? VwapS1Mio { get; set; }
    [ProtoMember(20)] public ulong? Systime { get; set; }
}

[ProtoContract]
public struct ObStatsFuturesRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public string? AssetCode { get; set; }
    [ProtoMember(4)] public double? MidPrice { get; set; }
    [ProtoMember(5)] public double? MicroPrice { get; set; }
    [ProtoMember(6)] public double? SpreadL1 { get; set; }
    [ProtoMember(7)] public double? SpreadL2 { get; set; }
    [ProtoMember(8)] public double? SpreadL3 { get; set; }
    [ProtoMember(9)] public double? SpreadL5 { get; set; }
    [ProtoMember(10)] public double? SpreadL10 { get; set; }
    [ProtoMember(11)] public double? SpreadL20 { get; set; }
    [ProtoMember(12)] public int? LevelsB { get; set; }
    [ProtoMember(13)] public int? LevelsS { get; set; }
    [ProtoMember(14)] public long? VolBL1 { get; set; }
    [ProtoMember(15)] public long? VolBL2 { get; set; }
    [ProtoMember(16)] public long? VolBL3 { get; set; }
    [ProtoMember(17)] public long? VolBL5 { get; set; }
    [ProtoMember(18)] public long? VolBL10 { get; set; }
    [ProtoMember(19)] public long? VolBL20 { get; set; }
    [ProtoMember(20)] public long? VolSL1 { get; set; }
    [ProtoMember(21)] public long? VolSL2 { get; set; }
    [ProtoMember(22)] public long? VolSL3 { get; set; }
    [ProtoMember(23)] public long? VolSL5 { get; set; }
    [ProtoMember(24)] public long? VolSL10 { get; set; }
    [ProtoMember(25)] public long? VolSL20 { get; set; }
    [ProtoMember(26)] public double? VwapBL3 { get; set; }
    [ProtoMember(27)] public double? VwapBL5 { get; set; }
    [ProtoMember(28)] public double? VwapBL10 { get; set; }
    [ProtoMember(29)] public double? VwapBL20 { get; set; }
    [ProtoMember(30)] public double? VwapSL3 { get; set; }
    [ProtoMember(31)] public double? VwapSL5 { get; set; }
    [ProtoMember(32)] public double? VwapSL10 { get; set; }
    [ProtoMember(33)] public double? VwapSL20 { get; set; }
    [ProtoMember(34)] public ulong? Systime { get; set; }
}

[ProtoContract]
public struct OrderStatsStockRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public int? PutOrdersB { get; set; }
    [ProtoMember(4)] public int? PutOrdersS { get; set; }
    [ProtoMember(5)] public double? PutValB { get; set; }
    [ProtoMember(6)] public double? PutValS { get; set; }
    [ProtoMember(7)] public int? PutVolB { get; set; }
    [ProtoMember(8)] public int? PutVolS { get; set; }
    [ProtoMember(9)] public double? PutVwapB { get; set; }
    [ProtoMember(10)] public double? PutVwapS { get; set; }
    [ProtoMember(11)] public int? PutVol { get; set; }
    [ProtoMember(12)] public double? PutVal { get; set; }
    [ProtoMember(13)] public int? PutOrders { get; set; }
    [ProtoMember(14)] public int? CancelOrdersB { get; set; }
    [ProtoMember(15)] public int? CancelOrdersS { get; set; }
    [ProtoMember(16)] public double? CancelValB { get; set; }
    [ProtoMember(17)] public double? CancelValS { get; set; }
    [ProtoMember(18)] public int? CancelVolB { get; set; }
    [ProtoMember(19)] public long? CancelVolS { get; set; }
    [ProtoMember(20)] public double? CancelVwapB { get; set; }
    [ProtoMember(21)] public double? CancelVwapS { get; set; }
    [ProtoMember(22)] public long? CancelVol { get; set; }
    [ProtoMember(23)] public double? CancelVal { get; set; }
    [ProtoMember(24)] public long? CancelOrders { get; set; }
    [ProtoMember(25)] public ulong? Systime { get; set; }
}

[ProtoContract]
public struct FutoiRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public string Clgroup { get; set; }
    [ProtoMember(4)] public int? SessId { get; set; }
    [ProtoMember(5)] public int? Seqnum { get; set; }
    [ProtoMember(6)] public long? Pos { get; set; }
    [ProtoMember(7)] public long? PosLong { get; set; }
    [ProtoMember(8)] public long? PosShort { get; set; }
    [ProtoMember(9)] public long? PosLongNum { get; set; }
    [ProtoMember(10)] public long? PosShortNum { get; set; }
    [ProtoMember(11)] public string? TradeSessionDate { get; set; }
    [ProtoMember(12)] public ulong? Systime { get; set; }
}

[ProtoContract]
public struct Hi2StockRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public string Metric { get; set; }
    [ProtoMember(4)] public double? Value { get; set; }
    [ProtoMember(5)] public string? Reference { get; set; }
    [ProtoMember(6)] public ulong? Systime { get; set; }
}

[ProtoContract]
public struct Hi2FuturesRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public string? AssetCode { get; set; }
    [ProtoMember(4)] public string Metric { get; set; }
    [ProtoMember(5)] public double? Value { get; set; }
    [ProtoMember(6)] public string? Reference { get; set; }
    [ProtoMember(7)] public ulong? Systime { get; set; }
}

[ProtoContract]
public struct MegaAlertsStockRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public string AlertType { get; set; }
    [ProtoMember(4)] public double? Threshold { get; set; }
    [ProtoMember(5)] public double? Value { get; set; }
    [ProtoMember(6)] public string? Reference { get; set; }
    [ProtoMember(7)] public ulong? Systime { get; set; }
}

[ProtoContract]
public struct MegaAlertsFuturesRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public ulong SourceTime { get; set; }
    [ProtoMember(3)] public string? AssetCode { get; set; }
    [ProtoMember(4)] public string AlertType { get; set; }
    [ProtoMember(5)] public double? Threshold { get; set; }
    [ProtoMember(6)] public double? Value { get; set; }
    [ProtoMember(7)] public string? Reference { get; set; }
    [ProtoMember(8)] public ulong? Systime { get; set; }
}

[ProtoSerializable(typeof(ObStatsStockRow))]
[ProtoSerializable(typeof(ObStatsFuturesRow))]
[ProtoSerializable(typeof(OrderStatsStockRow))]
[ProtoSerializable(typeof(FutoiRow))]
[ProtoSerializable(typeof(Hi2StockRow))]
[ProtoSerializable(typeof(Hi2FuturesRow))]
[ProtoSerializable(typeof(MegaAlertsStockRow))]
[ProtoSerializable(typeof(MegaAlertsFuturesRow))]
public partial class MarketProtobufModel : TypeModel
{
}
