using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts.Dto
{
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
