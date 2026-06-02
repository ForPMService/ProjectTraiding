namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Расписание торговой сессии срочного рынка на дату.
    ///
    /// Источник: GET /iss/calendars/futures/session.json
    /// Таблица: session_schedule
    ///
    /// Отличия от stock session:
    /// — нет колонки tradingsession;
    /// — колонка trade_session_date вместо tradedate;
    /// — time_from/time_till приходят как datetime (не time).
    ///
    /// Пример:
    /// RFUD, "-", oa_booking, 2026-05-07 08:50:00 — 2026-05-07 08:59:00.
    /// "-", "-", main_session, 2026-05-07 10:00:00 — 2026-05-07 19:00:00.
    /// </summary>
    public record CalendarFuturesSessionDTO
    {
        /// <summary>
        /// Дата торговой сессии.
        ///
        /// MOEX столбец: trade_session_date
        /// MOEX type: date
        ///
        /// Пример: 2026-05-07
        /// </summary>
        public string? TradeSessionDate { get; init; }

        /// <summary>
        /// Код режима торгов.
        ///
        /// MOEX столбец: boardid
        /// MOEX type: string
        ///
        /// "RFUD" — конкретный режим, "-" — общее расписание.
        /// </summary>
        public string? BoardId { get; init; }

        /// <summary>
        /// Код инструмента.
        ///
        /// MOEX столбец: secid
        /// MOEX type: string
        ///
        /// "-" — расписание для всех инструментов.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Тип временного слота.
        ///
        /// MOEX столбец: type
        /// MOEX type: string
        ///
        /// Расшифровка в CalendarSessionTypeDTO.
        /// Примеры: oa_booking, oa_pricing, morning_session, main_session,
        /// evening_session, settlement_session, clearing_session.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>
        /// Время начала слота (полная дата+время).
        ///
        /// MOEX столбец: time_from
        /// MOEX type: datetime
        ///
        /// Пример: 2026-05-07 08:50:00
        /// </summary>
        public DateTime? TimeFrom { get; init; }

        /// <summary>
        /// Время окончания слота (полная дата+время).
        ///
        /// MOEX столбец: time_till
        /// MOEX type: datetime
        ///
        /// null — окончание не определено (settlement, clearing).
        /// </summary>
        public DateTime? TimeTill { get; init; }

        /// <summary>
        /// Время обновления записи.
        ///
        /// MOEX столбец: updatetime
        /// MOEX type: datetime
        /// </summary>
        public DateTime? UpdateTime { get; init; }
    }
}