namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    public record CalendarDayWriteDTO
    {
        public DateOnly TradeDate { get; init; }
        public string Market { get; init; } = string.Empty;
        public int IsTraded { get; init; }
        public DateOnly? TradeSessionDate { get; init; }
        public string? Reason { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? StopTime { get; init; }
        public string DataSource { get; init; } = string.Empty;
        public DateTime? MoexUpdateTime { get; init; }
    }

    public record InstrumentBoardIntervalDTO
    {
        public string Market { get; init; } = string.Empty;
        public string SecId { get; init; } = string.Empty;
        public string BoardId { get; init; } = string.Empty;
        public DateOnly ValidFrom { get; init; }
        public DateOnly? ValidTill { get; init; }
    }

    public record FuturesExpirationDTO
    {
        public string SecId { get; init; } = string.Empty;
        public string? AssetCode { get; init; }
        public DateOnly ExpirationDate { get; init; }
        public string? ExpirationType { get; init; }
        public DateOnly? EndDate { get; init; }
        public short? WeekendSession { get; init; }
    }

    public record SplitWriteDTO
    {
        public DateOnly TradeDate { get; init; }
        public string SecId { get; init; } = string.Empty;
        public int BeforeQty { get; init; }
        public int AfterQty { get; init; }
    }

    public record ManualEventWriteDTO
    {
        public string SecId { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public DateOnly EventDate { get; init; }
        public DateOnly KnownFrom { get; init; }
        public DateOnly? RecordDate { get; init; }
        public DateOnly? LastTradeDate { get; init; }
        public DateOnly? PaymentDate { get; init; }
        public decimal? Amount { get; init; }
        public string? Currency { get; init; }
        public string? SourceNote { get; init; }
    }
}
