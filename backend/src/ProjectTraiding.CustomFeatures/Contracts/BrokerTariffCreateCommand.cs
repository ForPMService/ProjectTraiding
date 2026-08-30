namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct BrokerTariffCreateCommand(
        string BrokerName,
        string TariffName,
        string Market,
        string FeeType,
        decimal FeeValue,
        DateOnly ValidFrom,
        string? FeeCurrency,
        decimal? MinFee,
        decimal? TurnoverThreshold,
        DateOnly? ValidTill,
        string? Comment);
}
