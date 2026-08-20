namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record CalendarLoadRequest
    {
        public DateOnly? DateFrom { get; init; }
        public DateOnly? DateTill { get; init; }
    }

    public sealed record CalendarDayOverrideRequest
    {
        public string? Market { get; init; }
        public DateOnly? Date { get; init; }
        public int? IsTraded { get; init; }
        public string? Note { get; init; }
    }

    public sealed record ManualEventCreateRequest
    {
        public string? SecId { get; init; }
        public string? EventType { get; init; }
        public DateOnly? EventDate { get; init; }
        public DateOnly? KnownFrom { get; init; }
        public DateOnly? RecordDate { get; init; }
        public DateOnly? LastTradeDate { get; init; }
        public DateOnly? PaymentDate { get; init; }
        public decimal? Amount { get; init; }
        public string? Currency { get; init; }
        public string? SourceNote { get; init; }
    }

    public sealed record CalendarOperationResponse(int RowsWritten);
}
