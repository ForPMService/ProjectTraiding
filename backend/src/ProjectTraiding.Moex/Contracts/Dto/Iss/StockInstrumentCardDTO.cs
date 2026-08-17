namespace ProjectTraiding.Moex.Contracts.Dto.Iss
{
    /// <summary>
    /// Карточка акции — статика + текущая цена из одного списочного ответа.
    ///
    /// Источник: GET /engines/stock/markets/shares/boards/tqbr/securities.json?iss.meta=off
    /// Режим: ISS (публичный, без ключа).
    /// Блок: securities (паспорт).
    ///
    /// Карточка конкретного инструмента = строка из списочного ответа по SECID.
    /// Отдельный пер-тикерный вызов не нужен (решение из MOEX_Cards_Handoff_v0_1).
    ///
    /// Probe: probe-13, 2026-06-01. Securities 13 из 27 колонок.
    /// </summary>
    public record StockInstrumentCardDTO
    {
        // ── securities (13 полей) ──────────────────────────────

        /// <summary>Код инструмента. MOEX: SECID [0]. Пример: "SBER".</summary>
        public string? SecId { get; init; }

        /// <summary>Код режима торгов. MOEX: BOARDID [1]. Пример: "TQBR".</summary>
        public string? BoardId { get; init; }

        /// <summary>Краткое название. MOEX: SHORTNAME [2]. Пример: "Сбербанк".</summary>
        public string? ShortName { get; init; }

        /// <summary>Размер лота. MOEX: LOTSIZE [4]. Пример: 10.</summary>
        public int? LotSize { get; init; }

        /// <summary>Статус торгов. MOEX: STATUS [6]. Пример: "A".</summary>
        public string? Status { get; init; }

        /// <summary>Количество знаков после запятой. MOEX: DECIMALS [8]. Пример: 2.</summary>
        public int? Decimals { get; init; }

        /// <summary>Полное название. MOEX: SECNAME [9]. Пример: "Сбербанк России ПАО ао".</summary>
        public string? SecName { get; init; }

        /// <summary>Минимальный шаг цены. MOEX: MINSTEP [14]. Пример: 0.01.</summary>
        public double? MinStep { get; init; }

        /// <summary>Объём выпуска. MOEX: ISSUESIZE [18]. Пример: 21586948000.</summary>
        public long? IssueSize { get; init; }

        /// <summary>ISIN. MOEX: ISIN [19]. Пример: "RU0009029540".</summary>
        public string? Isin { get; init; }

        /// <summary>Валюта. MOEX: CURRENCYID [23]. Пример: "SUR".</summary>
        public string? Currency { get; init; }

        /// <summary>Тип ценной бумаги. MOEX: SECTYPE [24]. Пример: "1".</summary>
        public string? SecType { get; init; }

        /// <summary>Уровень листинга. MOEX: LISTLEVEL [25]. Пример: 1.</summary>
        public int? ListLevel { get; init; }

    }
}
