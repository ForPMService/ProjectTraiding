namespace ProjectTraiding.Moex.Contracts.Dto.Iss
{
    public record EngineDailyTableDTO
    {
        public string? TradeDate { get; init; }
        public string? StartTime { get; init; }
        public string? StopTime { get; init; }
    }

    public record ListingIntervalDTO
    {
        public string? SecId { get; init; }
        public string? BoardId { get; init; }
        public string? HistoryFrom { get; init; }
        public string? HistoryTill { get; init; }
    }

    public record SplitDTO
    {
        public string? TradeDate { get; init; }
        public string? SecId { get; init; }
        public int? BeforeQty { get; init; }
        public int? AfterQty { get; init; }
    }
}
