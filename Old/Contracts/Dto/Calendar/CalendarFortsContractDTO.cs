namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Фьючерсный контракт — справочник серий и экспираций.
    ///
    /// Источник: GET /iss/calendars/futures/securities.json
    /// Таблица: forts
    ///
    /// Одна строка — один фьючерсный контракт.
    /// Содержит код, базовый актив, дату экспирации, тип исполнения.
    ///
    /// Пример:
    /// CNYRUBF, CNYRUBTOM, "Однодневный фьючерсный контракт...", 2100-01-01.
    /// SiM6, Si, "Фьючерсный контракт на курс доллар/рубль", 2026-06-18.
    /// </summary>
    public record CalendarFortsContractDTO
    {
        /// <summary>
        /// Код инструмента (тикер фьючерса).
        ///
        /// MOEX столбец: secid
        /// MOEX type: string
        ///
        /// Пример: SiM6, BRN6, CNYRUBF.
        /// </summary>
        public string? SecId { get; init; }

        /// <summary>
        /// Код базового актива.
        ///
        /// MOEX столбец: asset_code
        /// MOEX type: string
        ///
        /// Пример: Si, BR, CNYRUBTOM, GAZPF.
        /// </summary>
        public string? AssetCode { get; init; }

        /// <summary>
        /// Короткое название контракта.
        ///
        /// MOEX столбец: shortname
        /// MOEX type: string
        ///
        /// Пример: CNYRUBF, EURRUBF, SiM6.
        /// </summary>
        public string? ShortName { get; init; }

        /// <summary>
        /// Тип исполнения контракта.
        ///
        /// MOEX столбец: exec_type
        /// MOEX type: string
        ///
        /// "S" — settlement (расчётный),
        /// другие значения — уточнять по документации.
        /// </summary>
        public string? ExecType { get; init; }

        /// <summary>
        /// Полное название контракта.
        ///
        /// MOEX столбец: contract_name
        /// MOEX type: undefined (строка)
        ///
        /// Пример: "Однодневный фьючерсный контракт с автопролонгацией
        /// на курс китайский юань - российский рубль".
        /// </summary>
        public string? ContractName { get; init; }

        /// <summary>
        /// Дата экспирации контракта.
        ///
        /// MOEX столбец: expiration_date
        /// MOEX type: date
        ///
        /// Пример: 2026-06-18.
        /// 2100-01-01 — бессрочный (автопролонгация).
        /// </summary>
        public string? ExpirationDate { get; init; }

        /// <summary>
        /// Дата окончания обращения.
        ///
        /// MOEX столбец: end_date
        /// MOEX type: date
        ///
        /// Часто совпадает с expiration_date.
        /// </summary>
        public string? EndDate { get; init; }

        /// <summary>
        /// Тип экспирации.
        ///
        /// MOEX столбец: expiration_type
        /// MOEX type: string
        ///
        /// "mc" — market close (?), "tc" — trading close (?).
        /// Точная семантика — по документации MOEX.
        /// </summary>
        public string? ExpirationType { get; init; }

        /// <summary>
        /// Время экспирации.
        ///
        /// MOEX столбец: expiration_time
        /// MOEX type: time
        ///
        /// Приходит как строка "HH:mm:ss".
        /// null — время не задано (бессрочные контракты).
        /// </summary>
        public string? ExpirationTime { get; init; }

        /// <summary>
        /// Доступен ли в weekend-сессии.
        ///
        /// MOEX столбец: weekend_session
        /// MOEX type: int32
        ///
        /// 0 — нет, 1 — да.
        /// </summary>
        public int? WeekendSession { get; init; }
    }
}