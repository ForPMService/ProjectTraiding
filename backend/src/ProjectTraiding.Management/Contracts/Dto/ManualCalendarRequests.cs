using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record ManualEventCreateRequest
    {
        public string? Secid { get; init; }
        public string? EventType { get; init; }
        public string? EventStage { get; init; }
        public DateOnly? EventDate { get; init; }
        public DateOnly? KnownFrom { get; init; }
        public DateOnly? RecordDate { get; init; }
        public DateOnly? LastTradeDate { get; init; }
        public DateOnly? PaymentDate { get; init; }
        public decimal? Amount { get; init; }
        public string? Currency { get; init; }
        public string? SourceNote { get; init; }
    }

    public sealed record ManualEventCreateResponse(Guid Id, int RowsWritten);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TradingPeriodCreateRequest
    {
        public DateOnly? TradeDate { get; init; }
        public string? Market { get; init; }
        public string? Boardid { get; init; }
        public string? Secid { get; init; }
        public short? Session { get; init; }
        public string? PeriodType { get; init; }
        public DateTime? TimeFrom { get; init; }
        public DateTime? TimeTill { get; init; }
        public string? Note { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TradingPeriodBatchCreateRequest(
        IReadOnlyList<TradingPeriodCreateRequest> Periods);

    public sealed record TradingPeriodBatchCreateResponse(IReadOnlyList<Guid> Ids);

}
