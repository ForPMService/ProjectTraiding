namespace History_DataMoex.Contracts.Dto.Algopack
{
    public record MegaAlertsFuturesDTO
    {
        /// <summary>
        /// Торговая дата.
        ///
        /// MOEX столбец: tradedate
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Торговое время.
        ///
        /// MOEX столбец: tradetime
        /// </summary>
        public string? TradeTime { get; init; }

        /// <summary>
        /// Код срочного инструмента.
        ///
        /// MOEX столбец: secid
        ///
        /// Пример:
        /// SiM6.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Код базового актива.
        ///
        /// MOEX столбец: asset_code
        ///
        /// Пример:
        /// Si.
        /// </summary>
        public string? AssetCode { get; init; }

        /// <summary>
        /// Тип алерта.
        ///
        /// MOEX столбец: alert_type
        ///
        /// Примеры:
        /// pr_change_99_9_pctl-,
        /// pr_low_min,
        /// vol_99_9_pctl,
        /// vol_s_99_9_pctl,
        /// net_vol_99_9_pctl-,
        /// oi_close_change_99_9_pctl-.
        /// </summary>
        public string? AlertType { get; init; }

        /// <summary>
        /// Пороговое значение, при превышении которого сработал алерт.
        ///
        /// MOEX столбец: threshold
        /// </summary>
        public double? Threshold { get; init; }

        /// <summary>
        /// Фактическое значение показателя.
        ///
        /// MOEX столбец: value
        /// </summary>
        public double? Value { get; init; }

        /// <summary>
        /// Справочная информация по алерту.
        ///
        /// MOEX столбец: reference
        ///
        /// В ответе приходит строка, внутри которой находится JSON.
        /// На этом уровне оставляем как string.
        /// </summary>
        public string? Reference { get; init; }

        /// <summary>
        /// Системное время формирования записи.
        ///
        /// MOEX столбец: SYSTIME
        /// </summary>
        public DateTime? SysTime { get; init; }
    }
}