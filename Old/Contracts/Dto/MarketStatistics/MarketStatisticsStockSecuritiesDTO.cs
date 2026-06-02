namespace ProjectTraiding.Moex.Contracts.Dto.MarketStatistics
{
    /// <summary>
    /// Расширенные справочные поля акции из MarketStatistics (securities-блок).
    ///
    /// Источник: GET /engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json?iss.only=securities
    /// Режим: APIM (платный, Bearer).
    /// Корневой блок: securities. Одна строка на тикер.
    ///
    /// Содержит только поля, отсутствующие в StockSecurityDTO (публичный ISS):
    /// MINSTEP, DECIMALS, ISIN, STATUS, LISTLEVEL, CURRENCYID, ISSUESIZE, SETTLEDATE.
    /// 10 из 27 колонок (с SECID и BOARDID для идентификации).
    /// </summary>
    public record MarketStatisticsStockSecuritiesDTO
    {
        /// <summary>Код инструмента. MOEX: SECID [0]. Пример: "SBER".</summary>
        public string? SECID { get; init; }

        /// <summary>Код режима торгов. MOEX: BOARDID [1]. Пример: "TQBR".</summary>
        public string? BOARDID { get; init; }

        /// <summary>Статус торгов: "A" — торгуется. MOEX: STATUS [6].</summary>
        public string? STATUS { get; init; }

        /// <summary>Количество знаков после запятой в цене. MOEX: DECIMALS [8]. Пример: 2.</summary>
        public int? DECIMALS { get; init; }

        /// <summary>Минимальный шаг цены. Критично для расчёта проскальзывания. MOEX: MINSTEP [14]. Пример: 0.01.</summary>
        public double? MINSTEP { get; init; }

        /// <summary>Объём выпуска (штук). MOEX: ISSUESIZE [18]. Пример: 21586948000.</summary>
        public long? ISSUESIZE { get; init; }

        /// <summary>Международный идентификатор. MOEX: ISIN [19]. Пример: "RU0009029540".</summary>
        public string? ISIN { get; init; }

        /// <summary>Валюта торгов. MOEX: CURRENCYID [23]. Пример: "SUR".</summary>
        public string? CURRENCYID { get; init; }

        /// <summary>Уровень листинга (1, 2, 3). MOEX: LISTLEVEL [25].</summary>
        public int? LISTLEVEL { get; init; }

        /// <summary>Дата расчётов. MOEX: SETTLEDATE [26]. Формат: "yyyy-MM-dd".</summary>
        public string? SETTLEDATE { get; init; }
    }
}
