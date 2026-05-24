namespace History_DataMoex.Contracts.Dto.Realtime
{
    /// <summary>
    /// Одна сделка по фьючерсу MOEX real-time.
    /// 
    /// Источник: ISS REST
    ///   /engines/futures/markets/forts/boards/RFUD/securities/{ticker}/trades.json
    /// 
    /// Root key: "trades"
    /// Колонок: 13
    /// 
    /// Важно: набор колонок отличается от акций.
    /// Futures имеет: BOARDNAME (не BOARDID), RECNO, OPENPOSITION, OFFMARKETDEAL.
    /// Futures не имеет: VALUE, PERIOD, TRADETIME_GRP, DECIMALS, TRADINGSESSION.
    /// 
    /// Ответ содержит до 5000 строк (лимит MOEX).
    /// Для догрузки: RECNO или TRADENO — Шаг 9 покажет, что надёжнее.
    /// </summary>
    public record RealtimeTradesFuturesDTO
    {
        /// <summary>
        /// Уникальный номер сделки.
        /// 
        /// MOEX столбец: TRADENO
        /// MOEX тип: int64
        /// 
        /// Внимание: на фьючерсах TRADENO — это 19-значное число.
        /// Пример: 1951779984234250241.
        /// </summary>
        public long? TradeNo { get; init; }

        /// <summary>
        /// Название режима торгов.
        /// 
        /// MOEX столбец: BOARDNAME
        /// MOEX тип: string
        /// 
        /// На фьючерсах приходит BOARDNAME, а не BOARDID.
        /// Пример: "RFUD"
        /// 
        /// Фактически значение совпадает с boardId,
        /// но имя колонки в контракте другое.
        /// </summary>
        public string? BoardName { get; init; }

        /// <summary>
        /// Код инструмента.
        /// 
        /// MOEX столбец: SECID
        /// MOEX тип: string (bytes: 12)
        /// 
        /// Пример: "SVM6", "SiM6"
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Торговая дата.
        /// 
        /// MOEX столбец: TRADEDATE
        /// MOEX тип: date (bytes: 10)
        /// 
        /// Пример: "2026-05-21"
        /// 
        /// На фьючерсах TRADEDATE стоит на позиции 3 (до TRADETIME),
        /// на акциях — на позиции 13 (после TRADINGSESSION).
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Время совершения сделки.
        /// 
        /// MOEX столбец: TRADETIME
        /// MOEX тип: time
        /// 
        /// Пример: "08:59:39"
        /// </summary>
        public string? TradeTime { get; init; }

        /// <summary>
        /// Цена сделки.
        /// 
        /// MOEX столбец: PRICE
        /// MOEX тип: double
        /// </summary>
        public double? Price { get; init; }

        /// <summary>
        /// Количество контрактов в сделке.
        /// 
        /// MOEX столбец: QUANTITY
        /// MOEX тип: int64
        /// </summary>
        public long? Quantity { get; init; }

        /// <summary>
        /// Системное время записи сделки.
        /// 
        /// MOEX столбец: SYSTIME
        /// MOEX тип: datetime (bytes: 19)
        /// 
        /// Пример: "2026-05-21 09:00:03"
        /// 
        /// На фьючерсах SYSTIME может отставать от TRADETIME
        /// (сделка в 08:59:39, запись в 09:00:03).
        /// </summary>
        public string? SysTime { get; init; }

        /// <summary>
        /// Порядковый номер записи в потоке данных MOEX.
        /// 
        /// MOEX столбец: RECNO
        /// MOEX тип: int64
        /// 
        /// Пример: 322684262148.
        /// 
        /// Монотонно растущий. Потенциальный ключ догрузки
        /// (альтернатива TRADENO — Шаг 9 покажет, что надёжнее).
        /// 
        /// Присутствует только в futures trades.
        /// </summary>
        public long? RecNo { get; init; }

        /// <summary>
        /// Открытый интерес после совершения сделки.
        /// 
        /// MOEX столбец: OPENPOSITION
        /// MOEX тип: int64
        /// 
        /// Пример: 716810.
        /// Количество открытых контрактов по инструменту.
        /// 
        /// Присутствует только в futures trades.
        /// </summary>
        public long? OpenPosition { get; init; }

        /// <summary>
        /// Признак внебиржевой сделки.
        /// 
        /// MOEX столбец: OFFMARKETDEAL
        /// MOEX тип: int32 (в JSON приходит как число)
        /// 
        /// 0 — биржевая сделка,
        /// 1 — внебиржевая.
        /// 
        /// Присутствует только в futures trades.
        /// </summary>
        public int? OffMarketDeal { get; init; }

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
        /// Дата торговой сессии.
        /// 
        /// MOEX столбец: TRADE_SESSION_DATE
        /// MOEX тип: date (bytes: 10)
        /// </summary>
        public string? TradeSessionDate { get; init; }
    }
}
