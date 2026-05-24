namespace ProjectTraiding.Moex.Contracts.Dto.Algopack
{
    /// <summary>
    /// Одна свеча MOEX ISS.
    /// 
    /// Свеча — это агрегат торгов за период времени:
    /// open, high, low, close, volume, value.
    /// 
    /// Пример:
    /// 1-минутная свеча Сбера с 07:00:00 по 07:00:59.
    /// </summary>
    public record CandlesDTO
    {
        /// <summary>
        /// Цена открытия свечи.
        /// 
        /// MOEX столбец: open
        /// 
        /// Это первая цена сделки внутри периода свечи.
        /// </summary>
        public double? Open { get; init; }

        /// <summary>
        /// Цена закрытия свечи.
        /// 
        /// MOEX столбец: close
        /// 
        /// Это последняя цена сделки внутри периода свечи.
        /// </summary>
        public double? Close { get; init; }

        /// <summary>
        /// Максимальная цена внутри свечи.
        /// 
        /// MOEX столбец: high
        /// 
        /// Нужна для анализа диапазона движения цены.
        /// </summary>
        public double? High { get; init; }

        /// <summary>
        /// Минимальная цена внутри свечи.
        /// 
        /// MOEX столбец: low
        /// 
        /// Нужна для анализа диапазона движения цены.
        /// </summary>
        public double? Low { get; init; }

        /// <summary>
        /// Денежный оборот за свечу.
        /// 
        /// MOEX столбец: value
        /// 
        /// Обычно это сумма сделок в деньгах:
        /// цена * количество.
        /// </summary>
        public double? Value { get; init; }

        /// <summary>
        /// Объём за свечу в штуках/лотах, как отдаёт источник.
        /// 
        /// MOEX столбец: volume
        /// 
        /// Нужен для анализа ликвидности и активности.
        /// </summary>
        public double? Volume { get; init; }

        /// <summary>
        /// Начало периода свечи.
        /// 
        /// MOEX столбец: begin
        /// 
        /// Пример:
        /// 2026-04-30 07:00:00
        /// </summary>
        public DateTime? Begin { get; init; }

        /// <summary>
        /// Конец периода свечи.
        /// 
        /// MOEX столбец: end
        /// 
        /// Пример:
        /// 2026-04-30 07:00:59
        /// </summary>
        public DateTime? End { get; init; }
    }
}