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

    public sealed record TradingPeriodCreateRequest
    {
        public string? Market { get; init; }
        public DateOnly? ValidFrom { get; init; }
        public DateOnly? ValidTill { get; init; }
        public string? Boardid { get; init; }
        public string? Secid { get; init; }
        public short? Session { get; init; }
        public string? PeriodType { get; init; }
        public DateTime? TimeFrom { get; init; }
        public DateTime? TimeTill { get; init; }
    }

    public sealed record TradingPeriodTypeCreateRequest
    {
        public string? Market { get; init; }
        public string? TypeCode { get; init; }
        public string? Title { get; init; }
    }
}
