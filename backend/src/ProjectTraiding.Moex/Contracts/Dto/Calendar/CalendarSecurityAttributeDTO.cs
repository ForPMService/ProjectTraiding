namespace History_DataMoex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Справочник атрибутов инструмента, которые могут изменяться.
    ///
    /// Источник: GET /iss/calendars/stock/securities/changes.json
    /// Таблица: securities.attributes
    ///
    /// 23 записи (по состоянию на май 2026).
    ///
    /// Пример:
    /// BUYBACKDATE, D, "Дата к которой рассчитывается доходность".
    /// COUPONDATE, D, "Дата выплаты купона".
    /// HASTECHNICALDEFAULT, N, "Наличие технического дефолта".
    /// </summary>
    public record CalendarSecurityAttributeDTO
    {
        /// <summary>
        /// Системное имя атрибута.
        ///
        /// MOEX столбец: name
        /// MOEX type: string
        ///
        /// Используется как ключ для связи с CalendarSecurityChangeDTO.AttributeName.
        /// Пример: BUYBACKDATE, COUPONDATE, SECID.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// Тип данных атрибута.
        ///
        /// MOEX столбец: type
        /// MOEX type: string
        ///
        /// Наблюдаемые значения:
        /// "D" — date / дата;
        /// "I" — integer / целое число;
        /// "N" — numeric / число;
        /// "S" — string / строка;
        /// "B" — boolean / булевый признак.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>
        /// Человекочитаемое описание атрибута.
        ///
        /// MOEX столбец: title
        /// MOEX type: string
        ///
        /// Пример: "Дата к которой рассчитывается доходность".
        /// </summary>
        public string? Title { get; init; }
    }
}