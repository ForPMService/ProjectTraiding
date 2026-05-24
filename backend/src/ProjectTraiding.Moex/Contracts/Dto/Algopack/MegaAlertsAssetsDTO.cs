namespace ProjectTraiding.Moex.Contracts.Dto.Algopack
{
    public record MegaAlertsAssetsDTO
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
        /// Код инструмента.
        ///
        /// MOEX столбец: secid
        ///
        /// Пример:
        /// SBER.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Тип алерта.
        ///
        /// MOEX столбец: alert_type
        ///
        /// Примеры:
        /// vol_s_99_9_pctl,
        /// net_vol_99_9_pctl-,
        /// vol_99_9_pctl,
        /// pr_change_99_9_pctl+.
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