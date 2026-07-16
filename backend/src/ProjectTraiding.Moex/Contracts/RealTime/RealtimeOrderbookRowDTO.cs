namespace ProjectTraiding.Moex.Contracts.Dto.Realtime
{
    /// <summary>
    /// Одна строка стакана котировок MOEX real-time.
    /// 
    /// Источник: ISS REST
    ///   stock  → /engines/stock/markets/shares/boards/TQBR/securities/{ticker}/orderbook.json
    ///   futures → /engines/futures/markets/forts/boards/RFUD/securities/{ticker}/orderbook.json
    /// 
    /// Root key: "orderbook"
    /// Колонок: 8
    /// Структура stock и futures идентична.
    /// 
    /// Стакан — это snapshot всех текущих заявок на покупку и продажу.
    /// Глубина стакана зависит от рынка/инструмента/параметров ответа.
    /// В текущих raw samples:
    ///   stock SBER: 10 уровней Buy + 10 уровней Sell;
    ///   futures SVM6: 20 уровней Buy + 20 уровней Sell.
    /// Каждая строка — один ценовой уровень.
    /// </summary>
    public record RealtimeOrderbookRowDTO
    {
        /// <summary>
        /// Идентификатор режима торгов.
        /// 
        /// MOEX столбец: BOARDID
        /// MOEX тип: string (bytes: 12)
        /// 
        /// Примеры:
        /// TQBR — Т+: Акции и ДР,
        /// RFUD — расчётные фьючерсы.
        /// </summary>
        public string? BoardId { get; init; }

        /// <summary>
        /// Код инструмента.
        /// 
        /// MOEX столбец: SECID
        /// MOEX тип: string (bytes: 12–36)
        /// 
        /// Примеры:
        /// SBER, GAZP, SVM6.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Направление заявки: покупка или продажа.
        /// 
        /// MOEX столбец: BUYSELL
        /// MOEX тип: string (bytes: 3)
        /// 
        /// Значения:
        /// "B" — Buy (bid),
        /// "S" — Sell (ask/offer).
        /// </summary>
        public string? BuySell { get; init; }

        /// <summary>
        /// Цена уровня стакана.
        /// 
        /// MOEX столбец: PRICE
        /// MOEX тип: double
        /// </summary>
        public double? Price { get; init; }

        /// <summary>
        /// Количество лотов/контрактов на данном ценовом уровне.
        /// 
        /// MOEX столбец: QUANTITY
        /// MOEX тип: int64
        /// </summary>
        public long? Quantity { get; init; }

        /// <summary>
        /// Порядковый номер обновления стакана.
        /// 
        /// MOEX столбец: SEQNUM
        /// MOEX тип: int64
        /// 
        /// Формат: YYYYMMDDHHmmss (как число).
        /// Используется для определения свежести snapshot.
        /// </summary>
        public long? SeqNum { get; init; }

        /// <summary>
        /// Время последнего обновления стакана.
        /// 
        /// MOEX столбец: UPDATETIME
        /// MOEX тип: time (bytes: 10)
        /// 
        /// Пример: "20:35:09"
        /// </summary>
        public string? UpdateTime { get; init; }

        /// <summary>
        /// Количество десятичных знаков для округления цены.
        /// 
        /// MOEX столбец: DECIMALS
        /// MOEX тип: int32 (stock) / int64 (futures)
        /// 
        /// Храним как long?, чтобы DTO покрывал оба metadata-варианта без приведения типа.
        /// </summary>
        public long? Decimals { get; init; }

        /// <summary>
        /// Дата торговой сессии снимка. Приходит не из строки orderbook, а из соседнего блока
        /// dataversion и проставляется приёмником перед записью снимка.
        /// </summary>
        public string? TradeSessionDate { get; init; }
    }
}
