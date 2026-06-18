using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record BrokerTariffCreateRequest
    {
        public string? BrokerName { get; init; }
        public string? TariffName { get; init; }
        public string? Market { get; init; }   // stock | futures
        public string? FeeType { get; init; }   // percent_of_turnover | fixed_per_contract | ...
        public decimal? FeeValue { get; init; }
        public string? FeeCurrency { get; init; }   // null → БД проставит DEFAULT 'RUB'
        public decimal? MinFee { get; init; }
        public decimal? TurnoverThreshold { get; init; }
        public string? ValidFrom { get; init; }   // "YYYY-MM-DD", парсит валидатор
        public string? ValidTill { get; init; }
        public string? Comment { get; init; }
    }
}
