namespace ProjectTraiding.Moex.Contracts.Dto.Realtime
{
    /// <summary>
    /// Одна сделка по акции MOEX real-time.
    /// 
    /// Источник: ISS REST
    ///   /engines/stock/markets/shares/boards/TQBR/securities/{ticker}/trades.json
    /// 
    /// Root key: "trades"
    /// Колонок: 14
    /// 
    /// Важно: набор колонок отличается от фьючерсов.
    /// Stock имеет: VALUE, PERIOD, DECIMALS, TRADINGSESSION, BOARDID.
    /// Futures имеет: BOARDNAME, RECNO, OPENPOSITION, OFFMARKETDEAL.
    /// Общий DTO невозможен без потери контракта.
    /// 
    /// Ответ содержит до 5000 строк (лимит MOEX).
    /// TRADENO — основной кандидат на ключ догрузки.
    /// Конкретный параметр запроса для догрузки проверяется
    /// диагностическим REST-опросом.
    /// </summary>
    public record RealtimeTradesStockDTO
    {
        /// <summary>
        /// Уникальный номер сделки.
        /// 
        /// MOEX столбец: TRADENO
        /// MOEX тип: int64
        /// 
        /// Монотонно растущий внутри торгового дня.
        /// Используется как ключ догрузки (previous_tradeno).
        /// </summary>
        public long? TradeNo { get; init; }

        /// <summary>
        /// Время совершения сделки.
        /// 
        /// MOEX столбец: TRADETIME
        /// MOEX тип: time (bytes: 10)
        /// 
        /// Пример: "06:59:53"
        /// </summary>
        public string? TradeTime { get; init; }

        /// <summary>
        /// Идентификатор режима торгов.
        /// 
        /// MOEX столбец: BOARDID
        /// MOEX тип: string (bytes: 12)
        /// 
        /// Пример: "TQBR"
        /// </summary>
        public string? BoardId { get; init; }

        /// <summary>
        /// Код инструмента.
        /// 
        /// MOEX столбец: SECID
        /// MOEX тип: string (bytes: 36)
        /// 
        /// Пример: "SBER"
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Цена сделки.
        /// 
        /// MOEX столбец: PRICE
        /// MOEX тип: double
        /// </summary>
        public double? Price { get; init; }

        /// <summary>
        /// Количество лотов в сделке.
        /// 
        /// MOEX столбец: QUANTITY
        /// MOEX тип: int64
        /// </summary>
        public long? Quantity { get; init; }

        /// <summary>
        /// Денежный объём сделки (цена × количество × лот).
        /// 
        /// MOEX столбец: VALUE
        /// MOEX тип: double
        /// 
        /// Присутствует только в stock trades.
        /// </summary>
        public double? Value { get; init; }

        /// <summary>
        /// Период торговой сессии.
        /// 
        /// MOEX столбец: PERIOD
        /// MOEX тип: string (bytes: 3)
        /// 
        /// Значения:
        /// "O" — открытие,
        /// "N" — основная сессия,
        /// "S" — pre-market / post-market,
        /// "C" — закрытие.
        /// </summary>
        public string? Period { get; init; }

        /// <summary>
        /// Системное время записи сделки.
        /// 
        /// MOEX столбец: SYSTIME
        /// MOEX тип: datetime (bytes: 19)
        /// 
        /// Пример: "2026-05-21 06:59:53"
        /// </summary>
        public string? SysTime { get; init; }

        /// <summary>
        /// Направление агрессора в сделке.
        /// 
        /// MOEX столбец: BUYSELL
        /// MOEX тип: string (bytes: 3)
        /// 
        /// "B" — покупка инициировала сделку,
        /// "S" — продажа инициировала сделку.
        /// </summary>
        public string? BuySell { get; init; }

        /// <summary>
        /// Количество десятичных знаков для округления цены.
        /// 
        /// MOEX столбец: DECIMALS
        /// MOEX тип: int32
        /// 
        /// Присутствует только в stock trades.
        /// </summary>
        public int? Decimals { get; init; }

        /// <summary>
        /// Идентификатор торговой сессии.
        /// 
        /// MOEX столбец: TRADINGSESSION
        /// MOEX тип: string (bytes: 3)
        /// 
        /// Пример: "0", "1".
        /// Присутствует только в stock trades.
        /// </summary>
        public string? TradingSession { get; init; }

        /// <summary>
        /// Торговая дата.
        /// 
        /// MOEX столбец: TRADEDATE
        /// MOEX тип: date (bytes: 10)
        /// 
        /// Пример: "2026-05-21"
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Дата торговой сессии.
        /// 
        /// MOEX столбец: TRADE_SESSION_DATE
        /// MOEX тип: date (bytes: 10)
        /// </summary>
        public string? TradeSessionDate { get; init; }
    }
}
