namespace ProjectTraiding.Moex.Contracts.Dto.Iss
{
    public record EngineDailyTableDTO
    {
        public DateOnly TradeDate { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? StopTime { get; init; }
    }

    public record ListingIntervalDTO
    {
        public string SecId { get; init; } = string.Empty;
        public string BoardId { get; init; } = string.Empty;
        public DateOnly? HistoryFrom { get; init; }
        public DateOnly? HistoryTill { get; init; }
    }
}
