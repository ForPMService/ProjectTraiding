namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct TradingPeriodCreateCommand(
        DateOnly TradeDate,
        string Market,
        string Boardid,
        string Secid,
        string PeriodType,
        DateTime TimeFrom,
        short? Session,
        DateTime? TimeTill,
        string? Note);
}
