namespace ProjectTraiding.Moex.Contracts.Dto.Algopack
{
    public record Hi2AssetDTO
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
        /// Название метрики HI2.
        ///
        /// MOEX столбец: metric
        ///
        /// Примеры:
        /// hhi_agressive,
        /// hhi_agressive_buy,
        /// hhi_agressive_sell,
        /// hhi_buy,
        /// hhi_sell,
        /// hhi_volume.
        /// </summary>
        public string? Metric { get; init; }

        /// <summary>
        /// Значение метрики.
        ///
        /// MOEX столбец: value
        /// </summary>
        public double? Value { get; init; }

        /// <summary>
        /// Справочная информация по метрике.
        ///
        /// MOEX столбец: reference
        ///
        /// В примере приходит пустая строка.
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