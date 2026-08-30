namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct ManualEventCreateCommand(
        string Secid,
        string EventType,
        string EventStage,
        DateOnly EventDate,
        DateOnly KnownFrom,
        DateOnly? RecordDate,
        DateOnly? LastTradeDate,
        DateOnly? PaymentDate,
        decimal? Amount,
        string? Currency,
        string? SourceNote);
}
