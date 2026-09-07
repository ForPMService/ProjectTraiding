namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct DividendEventUpdateCommand(
        Guid Id,
        string Secid,
        string EventType,
        DateOnly EventDate,
        DateTime KnownAt,
        decimal? DividendAmount,
        string? Currency,
        DateOnly? RecordDate,
        DateOnly? LastEligibleTradeDate,
        DateOnly? PaymentDate,
        string? SourceNote);
}
