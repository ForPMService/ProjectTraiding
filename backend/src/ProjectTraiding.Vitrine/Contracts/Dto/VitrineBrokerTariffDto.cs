using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    public sealed record VitrineBrokerTariffDto(
        long Id,
        string BrokerName,
        string TariffName,
        string Market,                 // stock | futures
        string FeeType,
        decimal FeeValue,
        string FeeCurrency,
        decimal? MinFee,
        decimal? TurnoverThreshold,
        DateOnly ValidFrom,
        DateOnly? ValidTill,
        string? Comment);
}
