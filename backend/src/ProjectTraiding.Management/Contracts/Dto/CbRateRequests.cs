using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts.Dto
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CbRateMeetingFactRequest
    {
        public Guid? CycleId { get; init; }
        public string? FactType { get; init; }
        public DateOnly? MeetingDate { get; init; }
        public DateTime? KnownAt { get; init; }
        public DateTime? ScheduledPublicationAt { get; init; }
        public DateTime? PublishedAt { get; init; }
        public decimal? RateBefore { get; init; }
        public decimal? RateAfter { get; init; }
        public DateOnly? EffectiveFrom { get; init; }
        public bool? IsCancelled { get; init; }
    }

    public sealed record CbRateMeetingFactCreateResponse(Guid Id, Guid CycleId);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CbRateRateBlock
    {
        public decimal? RateAnnounced { get; init; }
        public decimal? RateEffective { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CbRateRoisfixBlock
    {
        public decimal? Roisfix1w { get; init; }
        public decimal? Roisfix2w { get; init; }
        public decimal? Roisfix1m { get; init; }
        public decimal? Roisfix2m { get; init; }
        public decimal? Roisfix3m { get; init; }
        public decimal? Roisfix6m { get; init; }
        public decimal? Roisfix1y { get; init; }
        public decimal? Roisfix2y { get; init; }
        public DateTime? KnownAt { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CbRateRuoniaBlock
    {
        public decimal? Ruonia { get; init; }
        public string? Status { get; init; }
        public DateTime? KnownAt { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CbRateExpertBlock
    {
        public decimal? ExpertRate { get; init; }
        public string? Source { get; init; }
        public DateTime? KnownAt { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CbRateCalendarDayRequest
    {
        public DateOnly? D { get; init; }
        public CbRateRateBlock? Rate { get; init; }
        public CbRateRoisfixBlock? Roisfix { get; init; }
        public CbRateRuoniaBlock? Ruonia { get; init; }
        public CbRateExpertBlock? Expert { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CbRateCalendarBatchRequest
    {
        public IReadOnlyList<CbRateCalendarDayRequest>? Days { get; init; }
    }
}
