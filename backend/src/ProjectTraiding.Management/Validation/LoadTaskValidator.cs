using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Contracts;
using ProjectTraiding.Moex.Infrastructure;

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
            ValidationResult result = new();

            if (string.IsNullOrWhiteSpace(request.Secid))
                result.Errors.Add("secid обязателен");

            if (!MoexDomainRules.IsMarket(request.Market))
                result.Errors.Add("market должен быть одним из: stock, futures");

            if (string.IsNullOrWhiteSpace(request.Boardid))
                result.Errors.Add("boardid обязателен");

            if (!MoexDomainRules.IsDataKind(request.DataKind))
            {
                result.Errors.Add($"data_kind должен быть одним из: {string.Join(", ", MoexDomainRules.DataKinds)}");
            }
            else if (!MoexDomainRules.IsMarketAllowedForDataKind(request.Market, request.DataKind))
            {
                result.Errors.Add(
                    $"для data_kind={request.DataKind} допустимы рынки: " +
                    $"{string.Join(", ", MoexDomainRules.GetAllowedMarketsForDataKind(request.DataKind!))}");
            }

            if (!MoexDomainRules.IsStorageTarget(request.StorageTarget))
                result.Errors.Add("storage_target должен быть одним из: clickhouse");

            if (MoexDomainRules.RequiresCandleInterval(request.DataKind))
            {
                if (request.CandleInterval is null)
                    result.Errors.Add("candle_interval обязателен для candles");
                else if (!MoexDomainRules.IsCandleInterval(request.CandleInterval))
                    result.Errors.Add("candle_interval должен быть 1, 10, 60 или 24");
            }
            else if (request.CandleInterval is not null)
            {
                result.Errors.Add("candle_interval допустим только для candles");
            }

            if (request.DateFrom > request.DateTill)
                result.Errors.Add("date_from не может быть позже date_till");

            if (request.DateTill >= MoexTime.Today)
                result.Errors.Add("date_till должен быть раньше сегодняшнего дня");

            return result;
        }
    }
}
