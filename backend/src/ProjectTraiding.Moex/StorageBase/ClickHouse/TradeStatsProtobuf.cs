using System.Buffers;
using ProtoBuf;
using ProtoBuf.Meta;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

/// <summary>Protobuf row matching moex_trade_stats_5m_stock column order.</summary>
[ProtoContract]
public struct TradeStatsStockRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public double SourceTime { get; set; }
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
    [ProtoMember(26)] public double? Systime { get; set; }
}

/// <summary>Protobuf row matching moex_trade_stats_5m_futures column order.</summary>
[ProtoContract]
public struct TradeStatsFuturesRow
{
    [ProtoMember(1)] public string Secid { get; set; }
    [ProtoMember(2)] public double SourceTime { get; set; }
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
    [ProtoMember(32)] public double? Systime { get; set; }
}

[ProtoSerializable(typeof(TradeStatsStockRow))]
[ProtoSerializable(typeof(TradeStatsFuturesRow))]
public partial class MarketProtobufModel : TypeModel
{
}

public static class TradeStatsStockProtobufSchema
{
    public const string MessageName = "TradeStatsStockRow";

    public const string Definition = """
        syntax = "proto3";
        message TradeStatsStockRow {
          bytes secid = 1;
          double source_time = 2;
          double pr_open = 3;
          double pr_high = 4;
          double pr_low = 5;
          double pr_close = 6;
          double pr_std = 7;
          int32 vol = 8;
          double val = 9;
          int32 trades = 10;
          double pr_vwap = 11;
          double pr_change = 12;
          int32 trades_b = 13;
          int32 trades_s = 14;
          double val_b = 15;
          double val_s = 16;
          int64 vol_b = 17;
          int64 vol_s = 18;
          double disb = 19;
          double pr_vwap_b = 20;
          double pr_vwap_s = 21;
          int32 sec_pr_open = 22;
          int32 sec_pr_high = 23;
          int32 sec_pr_low = 24;
          int32 sec_pr_close = 25;
          double systime = 26;
        }
        """;
}

public static class TradeStatsFuturesProtobufSchema
{
    public const string MessageName = "TradeStatsFuturesRow";

    public const string Definition = """
        syntax = "proto3";
        message TradeStatsFuturesRow {
          bytes secid = 1;
          double source_time = 2;
          bytes asset_code = 3;
          double pr_open = 4;
          double pr_high = 5;
          double pr_low = 6;
          double pr_close = 7;
          double pr_std = 8;
          int64 vol = 9;
          int64 val = 10;
          int32 trades = 11;
          double pr_vwap = 12;
          double pr_change = 13;
          int32 trades_b = 14;
          int32 trades_s = 15;
          double val_b = 16;
          double val_s = 17;
          int64 vol_b = 18;
          int64 vol_s = 19;
          double disb = 20;
          double pr_vwap_b = 21;
          double pr_vwap_s = 22;
          double im = 23;
          int64 oi_open = 24;
          int64 oi_high = 25;
          int64 oi_low = 26;
          int64 oi_close = 27;
          int32 sec_pr_open = 28;
          int32 sec_pr_high = 29;
          int32 sec_pr_low = 30;
          int32 sec_pr_close = 31;
          double systime = 32;
        }
        """;
}

/// <summary>Writes generated-model Protobuf rows as length-delimited messages.</summary>
public static class TradeStatsProtobufBatchWriter
{
    public static void AppendStock(IBufferWriter<byte> destination, TradeStatsStockRow row)
    {
        using MeasureState<TradeStatsStockRow> measured = MarketProtobufModel.Instance.Measure(row);
        WriteVarint(destination, checked((ulong)measured.Length));
        measured.Serialize(destination);
    }

    public static void AppendFutures(IBufferWriter<byte> destination, TradeStatsFuturesRow row)
    {
        using MeasureState<TradeStatsFuturesRow> measured = MarketProtobufModel.Instance.Measure(row);
        WriteVarint(destination, checked((ulong)measured.Length));
        measured.Serialize(destination);
    }

    private static void WriteVarint(IBufferWriter<byte> destination, ulong value)
    {
        Span<byte> span = destination.GetSpan(10);
        int length = 0;

        while (value >= 0x80)
        {
            span[length++] = (byte)(value | 0x80);
            value >>= 7;
        }

        span[length++] = (byte)value;
        destination.Advance(length);
    }
}
