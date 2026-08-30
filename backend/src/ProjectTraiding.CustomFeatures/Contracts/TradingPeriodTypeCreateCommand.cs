namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct TradingPeriodTypeCreateCommand(
        string Market,
        string TypeCode,
        string Title);
}
