namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    public record CalendarStockSessionDTO
    {
        public string? TradeDate { get; init; }
        public int? TradingSession { get; init; }
        public string? BoardId { get; init; }
        public string? SecId { get; init; }
        public string? Type { get; init; }
        public string? TimeFrom { get; init; }
        public string? TimeTill { get; init; }
        public DateTime? UpdateTime { get; init; }
    }

    public record CalendarFuturesSessionDTO
    {
        public string? TradeSessionDate { get; init; }
        public string? BoardId { get; init; }
        public string? SecId { get; init; }
        public string? Type { get; init; }
        public DateTime? TimeFrom { get; init; }
        public DateTime? TimeTill { get; init; }
        public DateTime? UpdateTime { get; init; }
    }

    public record CalendarSessionTypeDTO
    {
        public string? Type { get; init; }
        public string? Title { get; init; }
    }
}
