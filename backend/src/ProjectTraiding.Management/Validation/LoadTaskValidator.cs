using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Contracts;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Loading.Planning;

namespace ProjectTraiding.Management.Validation
{
    /// <summary>
    /// Проверка формы запроса создания задачи до записи в базу. Словари и совместимость
    /// значений принадлежат MoexDomainRules; FK на инструмент проверит база.
    /// </summary>
    public static class LoadTaskValidator
    {
        public static ValidationResult Validate(LoadTaskCreateRequest request)
        {
            return Validate(
                request.Secid,
                request.Market,
                request.Boardid,
                request.DataKind,
                request.CandleInterval,
                request.DateFrom,
                request.DateTill);
        }

        public static ValidationResult Validate(MoexLoadWindow window)
        {
            return Validate(
                window.Secid,
                window.Market,
                window.Boardid,
                window.DataKind,
                window.CandleInterval,
                window.DateFrom,
                window.DateTill);
        }

        private static ValidationResult Validate(
            string secid,
            string market,
            string boardid,
            string dataKind,
            int? candleInterval,
            DateOnly dateFrom,
            DateOnly dateTill)
        {
            ValidationResult result = new();

            if (string.IsNullOrWhiteSpace(secid))
                result.Errors.Add("secid обязателен");

            if (!MoexDomainRules.IsMarket(market))
                result.Errors.Add("market должен быть одним из: stock, futures");

            if (string.IsNullOrWhiteSpace(boardid))
                result.Errors.Add("boardid обязателен");

            if (!MoexDomainRules.IsDataKind(dataKind))
            {
                result.Errors.Add($"data_kind должен быть одним из: {string.Join(", ", MoexDomainRules.DataKinds)}");
            }
            else if (!MoexDomainRules.IsMarketAllowedForDataKind(market, dataKind))
            {
                result.Errors.Add(
                    $"для data_kind={dataKind} допустимы рынки: " +
                    $"{string.Join(", ", MoexDomainRules.GetAllowedMarketsForDataKind(dataKind))}");
            }

            if (MoexDomainRules.RequiresCandleInterval(dataKind))
            {
                if (candleInterval is null)
                    result.Errors.Add("candle_interval обязателен для candles");
                else if (!MoexDomainRules.IsCandleInterval(candleInterval))
                    result.Errors.Add("candle_interval должен быть 1, 10, 60 или 24");
            }
            else if (candleInterval is not null)
            {
                result.Errors.Add("candle_interval допустим только для candles");
            }

            if (dateFrom > dateTill)
                result.Errors.Add("date_from не может быть позже date_till");

            if (dateTill >= MoexTime.Today)
                result.Errors.Add("date_till должен быть раньше сегодняшнего дня");

            return result;
        }
    }
}
