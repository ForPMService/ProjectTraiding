namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Неторговый/особый день для одного рынка (ФР или СР).
    ///
    /// Источник:
    /// ФР — GET /iss/calendars/stock.json
    /// СР — GET /iss/calendars/futures.json
    /// Таблица: off_days
    ///
    /// Одна строка на дату.
    /// Структура колонок идентична для stock и futures.
    ///
    /// Reason:
    /// "H" — праздник (holiday), биржа закрыта;
    /// "W" — выходной (weekend), торги идут в рамках weekend-сессии.
    /// </summary>
    public record CalendarOffDaysMarketDTO
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
        /// Торгуется ли в этот день.
        ///
        /// MOEX столбец: is_traded
        /// MOEX type: int32
        ///
        /// 0 — нет торгов (праздник);
        /// 1 — торги идут (weekend-сессия).
        /// </summary>
        public int? IsTraded { get; init; }

        /// <summary>
        /// Дата торговой сессии, к которой привязан этот день.
        ///
        /// MOEX столбец: trade_session_date
        /// MOEX type: date
        ///
        /// Пример: суббота 2026-01-17 привязана к сессии 2026-01-19.
        /// null, если торгов нет (is_traded = 0).
        /// </summary>
        public string? TradeSessionDate { get; init; }

        /// <summary>
        /// Причина нерабочего/особого дня.
        ///
        /// MOEX столбец: reason
        /// MOEX type: string
        ///
        /// "H" — праздник, "W" — выходной с торгами.
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Время обновления записи.
        ///
        /// MOEX столбец: updatetime
        /// MOEX type: datetime
        /// </summary>
        public DateTime? UpdateTime { get; init; }
    }
}