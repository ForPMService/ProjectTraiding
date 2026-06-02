namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Справочник причин приостановки торгов.
    ///
    /// Источник: GET /iss/calendars/stock/securities/suspended/details.json
    /// Таблица: suspended.reasons
    ///
    /// 28 записей (по состоянию на май 2026).
    ///
    /// Пример:
    /// 1 — "Торги не проводятся в дату погашения облигаций".
    /// 5001 — (значение из данных, конкретный текст уточнять).
    /// </summary>
    public record CalendarSuspendedReasonDTO
    {
        /// <summary>
        /// Числовой идентификатор причины.
        ///
        /// MOEX столбец: id
        /// MOEX type: int32
        ///
        /// Используется как ключ для связи с CalendarSuspendedDTO.ReasonId.
        /// </summary>
        public int? Id { get; init; }

        /// <summary>
        /// Текстовое описание причины.
        ///
        /// MOEX столбец: title
        /// MOEX type: string
        ///
        /// Пример: "Торги не проводятся в дату погашения облигаций".
        /// </summary>
        public string? Title { get; init; }
    }
}