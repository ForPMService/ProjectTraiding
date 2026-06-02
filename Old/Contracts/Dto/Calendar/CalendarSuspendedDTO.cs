namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Запрет/приостановка торгов по инструменту.
    ///
    /// Источник: GET /iss/calendars/stock/securities/suspended/details.json
    /// Таблица: suspended
    ///
    /// Одна строка — один период приостановки для одного инструмента.
    /// Причина расшифровывается через CalendarSuspendedReasonDTO по reason_id.
    ///
    /// Пагинация: suspended.cursor (INDEX/TOTAL/PAGESIZE).
    /// Total может быть очень большим (160k+).
    ///
    /// Пример:
    /// AGNC-RM, 5002, 2026-01-05, null, MPTR, Y2-14.
    /// </summary>
    public record CalendarSuspendedDTO
    {
        /// <summary>
        /// Код инструмента.
        ///
        /// MOEX столбец: secid
        /// MOEX type: string
        ///
        /// Пример: AGNC-RM, AMEZ, SBER.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Код причины приостановки.
        ///
        /// MOEX столбец: reason_id
        /// MOEX type: string (!)
        ///
        /// Внимание: metadata говорит string, хотя значения числовые.
        /// Расшифровка в CalendarSuspendedReasonDTO.
        /// Пример: "5002", "5001", "1".
        /// </summary>
        public string? ReasonId { get; init; }

        /// <summary>
        /// Дата начала приостановки.
        ///
        /// MOEX столбец: date_from
        /// MOEX type: date
        ///
        /// Пример: 2026-01-05.
        /// </summary>
        public string? DateFrom { get; init; }

        /// <summary>
        /// Дата окончания приостановки.
        ///
        /// MOEX столбец: date_till
        /// MOEX type: date
        ///
        /// null — приостановка бессрочная / дата окончания не определена.
        /// </summary>
        public string? DateTill { get; init; }

        /// <summary>
        /// Код режима торгов.
        ///
        /// MOEX столбец: boardid
        /// MOEX type: string
        ///
        /// Пример: MPTR, PSEQ.
        /// null — приостановка по всем режимам.
        /// </summary>
        public string? BoardId { get; init; }

        /// <summary>
        /// Коды расчётных систем.
        ///
        /// MOEX столбец: settle_codes
        /// MOEX type: string
        ///
        /// Пример: "Y2-14", "Z0".
        /// null — не указаны.
        /// </summary>
        public string? SettleCodes { get; init; }

        /// <summary>
        /// Дата изменения записи.
        ///
        /// MOEX столбец: changedate
        /// MOEX type: date
        ///
        /// Пример: 2025-12-30.
        /// </summary>
        public string? ChangeDate { get; init; }

        /// <summary>
        /// Время обновления записи.
        ///
        /// MOEX столбец: updatetime
        /// MOEX type: datetime
        /// </summary>
        public DateTime? UpdateTime { get; init; }
    }
}