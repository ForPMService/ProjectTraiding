using ClickHouse.Driver;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

/// <summary>
/// Типизированная строка минутной свечи для прямой вставки в ClickHouse.
/// </summary>
public sealed class MoexCandleDTO
{
    [ClickHouseColumn(Name = "secid", Type = "LowCardinality(String)")]
    public string SecId { get; init; } = string.Empty;

    [ClickHouseColumn(Name = "open", Type = "Nullable(Float64)")]
    public double? Open { get; init; }

    [ClickHouseColumn(Name = "close", Type = "Nullable(Float64)")]
    public double? Close { get; init; }

    [ClickHouseColumn(Name = "high", Type = "Nullable(Float64)")]
    public double? High { get; init; }

    [ClickHouseColumn(Name = "low", Type = "Nullable(Float64)")]
    public double? Low { get; init; }

    [ClickHouseColumn(Name = "value", Type = "Nullable(Float64)")]
    public double? Value { get; init; }

    [ClickHouseColumn(Name = "volume", Type = "Nullable(Float64)")]
    public double? Volume { get; init; }

    [ClickHouseColumn(Name = "begin", Type = "DateTime64(3, 'Europe/Moscow')")]
    public DateTime Begin { get; init; }

    [ClickHouseColumn(Name = "end", Type = "Nullable(DateTime64(3, 'Europe/Moscow'))")]
    public DateTime? End { get; init; }

    [ClickHouseColumn(Name = "ingest_priority", Type = "UInt8")]
    public byte IngestPriority { get; init; }
}
