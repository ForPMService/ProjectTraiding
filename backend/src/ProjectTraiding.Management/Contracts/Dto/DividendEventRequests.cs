using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts.Dto
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DividendEventCreateRequest
    {
        public string? Secid { get; init; }
        public string? EventType { get; init; }
        public DateOnly? EventDate { get; init; }
        public DateTime? KnownAt { get; init; }
        public bool? IsCancelled { get; init; }
        public decimal? DividendAmount { get; init; }
        public string? Currency { get; init; }
        public DateOnly? RecordDate { get; init; }
        public DateOnly? LastEligibleTradeDate { get; init; }
        public DateOnly? PaymentDate { get; init; }
        public string? SourceNote { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DividendEventBatchCreateRequest(
        IReadOnlyList<DividendEventCreateRequest> Events);

    public sealed record DividendEventBatchCreateResponse(IReadOnlyList<Guid> Ids);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DividendEventUpdateRequest
    {
        public string? Secid { get; init; }
        public string? EventType { get; init; }
        public DateOnly? EventDate { get; init; }
        public DateTime? KnownAt { get; init; }
        public decimal? DividendAmount { get; init; }
        public string? Currency { get; init; }
        public DateOnly? RecordDate { get; init; }
        public DateOnly? LastEligibleTradeDate { get; init; }
        public DateOnly? PaymentDate { get; init; }
        public string? SourceNote { get; init; }
    }
}
