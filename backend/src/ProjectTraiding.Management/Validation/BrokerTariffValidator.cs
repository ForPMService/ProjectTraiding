using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Contracts;

namespace ProjectTraiding.Management.Validation
{
    public static class BrokerTariffValidator
    {
        private static readonly string[] FeeTypes =
        { "percent_of_turnover", "fixed_per_contract", "fixed_per_trade", "monthly", "depository" };

        public static ValidationResult Validate(BrokerTariffCreateRequest request)
        {
            ValidationResult result = new();
            DateOnly from = default;
            DateOnly till = default;
            bool hasFrom = false;
            bool hasTill = false;

            if (string.IsNullOrWhiteSpace(request.BrokerName))
                result.Errors.Add("broker_name обязателен");
            if (string.IsNullOrWhiteSpace(request.TariffName))
                result.Errors.Add("tariff_name обязателен");
            if (!MoexDomainRules.IsMarket(request.Market))
                result.Errors.Add("market должен быть одним из: stock, futures");
            if (request.FeeType is null || Array.IndexOf(FeeTypes, request.FeeType) < 0)
                result.Errors.Add($"fee_type должен быть одним из: {string.Join(", ", FeeTypes)}");
            if (request.FeeValue is null)
                result.Errors.Add("fee_value обязателен");

            if (string.IsNullOrWhiteSpace(request.ValidFrom))
                result.Errors.Add("valid_from обязателен");
            else if (!DateOnly.TryParse(request.ValidFrom, out from))
                result.Errors.Add("valid_from: неверный формат даты (YYYY-MM-DD)");
            else
                hasFrom = true;

            if (request.ValidTill is not null)
            {
                if (!DateOnly.TryParse(request.ValidTill, out till))
                    result.Errors.Add("valid_till: неверный формат даты (YYYY-MM-DD)");
                else
                    hasTill = true;
            }

            if (hasFrom && hasTill && till < from)
                result.Errors.Add("valid_till должен быть ≥ valid_from");

            return result;
        }
    }
}
