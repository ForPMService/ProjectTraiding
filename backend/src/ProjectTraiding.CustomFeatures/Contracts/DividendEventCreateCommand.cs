namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct DividendEventCreateCommand(
        string Secid,
        string EventType,
        DateOnly EventDate,
        DateTime KnownAt,
        bool IsCancelled,
        decimal? DividendAmount,
        string? Currency,
        DateOnly? RecordDate,
        DateOnly? LastEligibleTradeDate,
        DateOnly? PaymentDate,
        string? SourceNote);
}
