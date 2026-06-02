namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Изменение параметра инструмента.
    ///
    /// Источник: GET /iss/calendars/stock/securities/changes.json
    /// Таблица: securities
    ///
    /// Одна строка — одно изменение одного атрибута одного инструмента.
    /// Какие атрибуты бывают — в CalendarSecurityAttributeDTO.
    ///
    /// Пагинация: securities.cursor (INDEX/TOTAL/PAGESIZE).
    ///
    /// Примеры:
    /// updated, ALNP, SECID, "ALNP" → "alnp" — переименование тикера.
    /// removed, RU000A0JWGV2, COUPONDATE, "2026-05-07" → null — купон погашен.
    /// updated, RU000A0JXR84, COUPONDATE, "2026-05-07" → "2026-11-05" — следующий купон.
    /// </summary>
    public record CalendarSecurityChangeDTO
    {
        /// <summary>
        /// Время изменения.
        ///
        /// MOEX столбец: updatetime
        /// MOEX type: datetime
        ///
        /// Пример: 2026-05-07 00:21:04.
        /// </summary>
        public DateTime? UpdateTime { get; init; }

        /// <summary>
        /// Тип действия.
        ///
        /// MOEX столбец: action
        /// MOEX type: string
        ///
        /// "updated" — атрибут изменился;
        /// "removed" — атрибут удалён (after_value = null).
        /// </summary>
        public string? Action { get; init; }

        /// <summary>
        /// Код инструмента.
        ///
        /// MOEX столбец: secid
        /// MOEX type: string
        ///
        /// Пример: SBER, RU000A0JWGV2.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Название атрибута, который изменился.
        ///
        /// MOEX столбец: attribute_name
        /// MOEX type: string
        ///
        /// Расшифровка в CalendarSecurityAttributeDTO.
        /// Примеры: SECID, COUPONDATE, HASTECHNICALDEFAULT.
        /// </summary>
        public string? AttributeName { get; init; }

        /// <summary>
        /// Значение атрибута до изменения.
        ///
        /// MOEX столбец: before_value
        /// MOEX type: string
        ///
        /// Всегда строка, даже для числовых/дат.
        /// Пример: "ALNP", "2026-05-07", "0".
        /// </summary>
        public string? BeforeValue { get; init; }

        /// <summary>
        /// Значение атрибута после изменения.
        ///
        /// MOEX столбец: after_value
        /// MOEX type: string
        ///
        /// null — атрибут удалён (action = removed).
        /// Пример: "alnp", "2026-11-05", "1".
        /// </summary>
        public string? AfterValue { get; init; }
    }
}