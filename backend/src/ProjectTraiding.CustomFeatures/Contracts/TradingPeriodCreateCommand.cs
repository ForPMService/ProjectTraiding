namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct TradingPeriodCreateCommand(
        string Market,
        DateOnly ValidFrom,
        DateOnly ValidTill,
        string Boardid,
        string PeriodType,
        DateTime TimeFrom,
        string? Secid,
        short? Session,
        DateTime? TimeTill);
}
