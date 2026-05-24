namespace History_DataMoex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Неторговый/особый день — сводка по всем рынкам в одной строке.
    ///
    /// Источник: GET /iss/calendars.json
    /// Таблица: off_days
    ///
    /// Одна строка на дату. Для каждого рынка (валютный, срочный, фондовый)
    /// указано: торгуется ли, к какой торговой сессии привязан, причина.
    ///
    /// Reason:
    /// "H" — праздник (holiday), биржа закрыта;
    /// "W" — выходной (weekend), но торги идут в рамках weekend-сессии;
    /// null — обычный выходной без торгов для этого рынка.
    /// </summary>
    public record CalendarOffDaysAllDTO
    {
        /// <summary>
        /// Календарная дата.
        ///
        /// MOEX столбец: tradedate
        /// MOEX type: date
        ///
        /// Пример: 2026-01-01
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Валютный рынок: торгуется ли в этот день.
        ///
        /// MOEX столбец: currency_workday
        /// MOEX type: int64
        ///
        /// 0 — нет торгов, 1 — торги идут (weekend-сессия).
        /// </summary>
        public long? CurrencyWorkday { get; init; }

        /// <summary>
        /// Валютный рынок: дата торговой сессии, к которой привязан этот день.
        ///
        /// MOEX столбец: currency_trade_session_date
        /// MOEX type: date
        ///
        /// null, если торгов нет.
        /// </summary>
        public string? CurrencyTradeSessionDate { get; init; }

        /// <summary>
        /// Валютный рынок: причина нерабочего дня.
        ///
        /// MOEX столбец: currency_reason
        /// MOEX type: string
        ///
        /// "H" — праздник, "W" — выходной с торгами, null — обычный выходной.
        /// </summary>
        public string? CurrencyReason { get; init; }

        /// <summary>
        /// Срочный рынок: торгуется ли в этот день.
        ///
        /// MOEX столбец: futures_workday
        /// MOEX type: int64
        ///
        /// 0 — нет торгов, 1 — торги идут (weekend-сессия).
        /// </summary>
        public long? FuturesWorkday { get; init; }

        /// <summary>
        /// Срочный рынок: дата торговой сессии, к которой привязан этот день.
        ///
        /// MOEX столбец: futures_trade_session_date
        /// MOEX type: date
        /// </summary>
        public string? FuturesTradeSessionDate { get; init; }

        /// <summary>
        /// Срочный рынок: причина нерабочего дня.
        ///
        /// MOEX столбец: futures_reason
        /// MOEX type: string
        /// </summary>
        public string? FuturesReason { get; init; }

        /// <summary>
        /// Фондовый рынок: торгуется ли в этот день.
        ///
        /// MOEX столбец: stock_workday
        /// MOEX type: int64
        ///
        /// 0 — нет торгов, 1 — торги идут (weekend-сессия).
        /// </summary>
        public long? StockWorkday { get; init; }

        /// <summary>
        /// Фондовый рынок: дата торговой сессии, к которой привязан этот день.
        ///
        /// MOEX столбец: stock_trade_session_date
        /// MOEX type: date
        /// </summary>
        public string? StockTradeSessionDate { get; init; }

        /// <summary>
        /// Фондовый рынок: причина нерабочего дня.
        ///
        /// MOEX столбец: stock_reason
        /// MOEX type: string
        /// </summary>
        public string? StockReason { get; init; }
    }
}