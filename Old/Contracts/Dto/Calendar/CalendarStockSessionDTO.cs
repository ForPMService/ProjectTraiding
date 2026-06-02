namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Расписание торговой сессии фондового рынка на дату.
    ///
    /// Источник: GET /iss/calendars/stock/session.json
    /// Таблица: session_schedule
    ///
    /// Одна строка — один временной слот для конкретного boardid/secid.
    /// Тип слота (type) расшифровывается в session_schedule.types.
    ///
    /// Пример:
    /// TQBR, "", oa_booking, 06:50:00–09:59:00 — аукцион открытия для всего TQBR.
    /// OCAR, "RU000A10AVD8", system, 18:30:00–23:49:59 — отдельное расписание для конкретного инструмента.
    /// </summary>
    public record CalendarStockSessionDTO
    {
        /// <summary>
        /// Торговая дата.
        ///
        /// MOEX столбец: tradedate
        /// MOEX type: date
        ///
        /// Пример: 2026-05-07
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Код торговой сессии.
        ///
        /// MOEX столбец: tradingsession
        /// MOEX type: int32
        ///
        /// Известные значения: -999 (по наблюдениям из данных).
        /// Точная семантика не документирована.
        /// </summary>
        public int? TradingSession { get; init; }

        /// <summary>
        /// Код режима торгов.
        ///
        /// MOEX столбец: boardid
        /// MOEX type: string
        ///
        /// Пример: TQBR, EQOB, OCAR, MPAU.
        /// </summary>
        public string? BoardId { get; init; }

        /// <summary>
        /// Код инструмента.
        ///
        /// MOEX столбец: secid
        /// MOEX type: string
        ///
        /// Пустая строка "" — расписание по умолчанию для всего boardid.
        /// Непустое — индивидуальное расписание для инструмента.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Тип временного слота.
        ///
        /// MOEX столбец: type
        /// MOEX type: string
        ///
        /// Расшифровка в CalendarSessionTypeDTO.
        /// Примеры: oa_booking, oa_pricing, system, ca_booking, ca_pricing.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>
        /// Время начала слота.
        ///
        /// MOEX столбец: time_from
        /// MOEX type: time
        ///
        /// Приходит как строка "HH:mm:ss".
        /// Пример: 06:50:00
        /// </summary>
        public string? TimeFrom { get; init; }

        /// <summary>
        /// Время окончания слота.
        ///
        /// MOEX столбец: time_till
        /// MOEX type: time
        ///
        /// Пример: 09:59:00
        /// null — окончание не определено (например, клиринг).
        /// </summary>
        public string? TimeTill { get; init; }

        /// <summary>
        /// Время обновления записи.
        ///
        /// MOEX столбец: updatetime
        /// MOEX type: datetime
        /// </summary>
        public DateTime? UpdateTime { get; init; }
    }
}