namespace ProjectTraiding.Moex.Contracts.Dto.Algopack
{
    /// <summary>
    /// Одна строка MOEX ALGOPACK TradeStats за 5 минут.
    /// 
    /// TradeStats — статистика реальных сделок.
    /// 
    /// Это не обычная свеча, а расширенная 5-минутная статистика:
    /// цена, объём, сделки, покупки, продажи, VWAP, дисбаланс.
    /// </summary>
    public record SuperCandlesTradeStats5mDTO
    {
        /// <summary>
        /// Торговая дата.
        /// 
        /// MOEX столбец: tradedate
        /// 
        /// Оставляем string?, потому что на текущем этапе проще
        /// сначала принять значение как есть, а потом отдельно разобрать.
        /// 
        /// Пример:
        /// 2026-04-30
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Время торговой точки.
        /// 
        /// MOEX столбец: tradetime
        /// 
        /// Для supercandles это метка конца 5-минутного интервала.
        /// 
        /// Пример:
        /// 07:05:00 означает интервал примерно 07:00:00–07:04:59.
        /// </summary>
        public string? TradeTime { get; init; }

        /// <summary>
        /// Код инструмента на MOEX.
        /// 
        /// MOEX столбец: secid
        /// 
        /// Пример:
        /// SBER
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Цена открытия 5-минутного интервала.
        /// 
        /// MOEX столбец: pr_open
        /// </summary>
        public double? PrOpen { get; init; }

        /// <summary>
        /// Максимальная цена внутри 5-минутного интервала.
        /// 
        /// MOEX столбец: pr_high
        /// </summary>
        public double? PrHigh { get; init; }

        /// <summary>
        /// Минимальная цена внутри 5-минутного интервала.
        /// 
        /// MOEX столбец: pr_low
        /// </summary>
        public double? PrLow { get; init; }

        /// <summary>
        /// Цена закрытия 5-минутного интервала.
        /// 
        /// MOEX столбец: pr_close
        /// </summary>
        public double? PrClose { get; init; }

        /// <summary>
        /// Стандартное отклонение цены внутри интервала.
        /// 
        /// MOEX столбец: pr_std
        /// 
        /// Простыми словами: насколько цена "болталась" внутри свечи.
        /// </summary>
        public double? PrStd { get; init; }

        /// <summary>
        /// Общий объём сделок.
        /// 
        /// MOEX столбец: vol
        /// 
        /// Тип в MOEX metadata: int32.
        /// </summary>
        public int? Vol { get; init; }

        /// <summary>
        /// Денежный оборот сделок.
        /// 
        /// MOEX столбец: val
        /// 
        /// Это объём в деньгах.
        /// </summary>
        public double? Val { get; init; }

        /// <summary>
        /// Количество сделок за интервал.
        /// 
        /// MOEX столбец: trades
        /// </summary>
        public int? Trades { get; init; }

        /// <summary>
        /// Средневзвешенная цена по объёму.
        /// 
        /// MOEX столбец: pr_vwap
        /// 
        /// VWAP — Volume Weighted Average Price.
        /// По-русски: средняя цена сделки с учётом объёма.
        /// </summary>
        public double? PrVwap { get; init; }

        /// <summary>
        /// Изменение цены за интервал.
        /// 
        /// MOEX столбец: pr_change
        /// 
        /// Нужна для оценки направления движения внутри интервала.
        /// </summary>
        public double? PrChange { get; init; }

        /// <summary>
        /// Количество сделок на покупку.
        /// 
        /// MOEX столбец: trades_b
        /// 
        /// B = buy.
        /// </summary>
        public int? TradesB { get; init; }

        /// <summary>
        /// Количество сделок на продажу.
        /// 
        /// MOEX столбец: trades_s
        /// 
        /// S = sell.
        /// </summary>
        public int? TradesS { get; init; }

        /// <summary>
        /// Денежный оборот покупок.
        /// 
        /// MOEX столбец: val_b
        /// </summary>
        public double? ValB { get; init; }

        /// <summary>
        /// Денежный оборот продаж.
        /// 
        /// MOEX столбец: val_s
        /// </summary>
        public double? ValS { get; init; }

        /// <summary>
        /// Объём покупок.
        /// 
        /// MOEX столбец: vol_b
        /// 
        /// Тип в MOEX metadata: int64.
        /// </summary>
        public long? VolB { get; init; }

        /// <summary>
        /// Объём продаж.
        /// 
        /// MOEX столбец: vol_s
        /// 
        /// Тип в MOEX metadata: int64.
        /// </summary>
        public long? VolS { get; init; }

        /// <summary>
        /// Дисбаланс покупок и продаж.
        /// 
        /// MOEX столбец: disb
        /// 
        /// Простыми словами:
        /// положительное значение — перевес покупок,
        /// отрицательное — перевес продаж.
        /// </summary>
        public double? Disb { get; init; }

        /// <summary>
        /// VWAP по покупкам.
        /// 
        /// MOEX столбец: pr_vwap_b
        /// 
        /// Средневзвешенная цена покупок.
        /// </summary>
        public double? PrVwapB { get; init; }

        /// <summary>
        /// VWAP по продажам.
        /// 
        /// MOEX столбец: pr_vwap_s
        /// 
        /// Средневзвешенная цена продаж.
        /// </summary>
        public double? PrVwapS { get; init; }

        /// <summary>
        /// Системное время формирования/публикации строки.
        /// 
        /// MOEX столбец: SYSTIME
        /// 
        /// Это НЕ рыночное время интервала.
        /// Для стыковки использовать TradeDate + TradeTime.
        /// </summary>
        public DateTime? SysTime { get; init; }

        /// <summary>
        /// Секунда внутри интервала, где была цена открытия.
        /// 
        /// MOEX столбец: sec_pr_open
        /// 
        /// Пока можно хранить, но не использовать в первой аналитике.
        /// </summary>
        public int? SecPrOpen { get; init; }

        /// <summary>
        /// Секунда внутри интервала, где был максимум цены.
        /// 
        /// MOEX столбец: sec_pr_high
        /// </summary>
        public int? SecPrHigh { get; init; }

        /// <summary>
        /// Секунда внутри интервала, где был минимум цены.
        /// 
        /// MOEX столбец: sec_pr_low
        /// </summary>
        public int? SecPrLow { get; init; }

        /// <summary>
        /// Секунда внутри интервала, где была цена закрытия.
        /// 
        /// MOEX столбец: sec_pr_close
        /// </summary>
        public int? SecPrClose { get; init; }
    }
}