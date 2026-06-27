using ProjectTraiding.Management.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Validation
{
    /// <summary>
    /// Проверка запроса создания задачи загрузки до записи в базу.
    /// Ловит вывернутый диапазон, рассогласование интервала с видом данных, недопустимые
    /// значения перечислений. FK на инструмент проверит база — здесь не дублируем.
    /// </summary>
    public static class LoadTaskValidator
    {
        private static readonly string[] Markets = { "stock", "futures" };
        private static readonly string[] StorageTargets = { "none", "file", "clickhouse" };
        private static readonly string[] DataKinds =
        { "candles", "tradestats", "obstats", "orderstats", "futoi", "hi2", "mega_alerts" };

        public static ValidationResult Validate(LoadTaskCreateRequest request)
        {
            ValidationResult result = new();

            if (string.IsNullOrWhiteSpace(request.Secid))
                result.Errors.Add("secid обязателен");

            if (request.Market is null || Array.IndexOf(Markets, request.Market) < 0)
                result.Errors.Add("market должен быть одним из: stock, futures");

            if (string.IsNullOrWhiteSpace(request.Boardid))
                result.Errors.Add("boardid обязателен");

            if (request.DataKind is null || Array.IndexOf(DataKinds, request.DataKind) < 0)
                result.Errors.Add($"data_kind должен быть одним из: {string.Join(", ", DataKinds)}");

            if (request.StorageTarget is null || Array.IndexOf(StorageTargets, request.StorageTarget) < 0)
                result.Errors.Add("storage_target должен быть одним из: none, file, clickhouse");

            // Интервал осмыслен только для свечей: для candles обязателен, для прочих запрещён.
            if (request.DataKind == "candles")
            {
                if (request.CandleInterval is null)
                    result.Errors.Add("candle_interval обязателен для candles");
                else if (request.CandleInterval is not (1 or 5 or 60))
                    result.Errors.Add("candle_interval должен быть 1, 5 или 60");
            }
            else if (request.CandleInterval is not null)
            {
                result.Errors.Add("candle_interval допустим только для candles");
            }

            // Вывернутый диапазон — отдельного CHECK на это в схеме нет.
            if (request.DateFrom > request.DateTill)
                result.Errors.Add("date_from не может быть позже date_till");

            return result;
        }
    }
}
