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

    public sealed record CalendarOperationResponse(int RowsWritten);
}
