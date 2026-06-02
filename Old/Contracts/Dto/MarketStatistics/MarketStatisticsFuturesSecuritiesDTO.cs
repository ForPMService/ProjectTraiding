namespace ProjectTraiding.Moex.Contracts.Dto.MarketStatistics
{
    /// <summary>
    /// Расширенные справочные поля фьючерса из MarketStatistics (securities-блок).
    ///
    /// Источник: GET /engines/futures/markets/forts/boards/RFUD/securities/{ticker}.json?iss.only=securities
    /// Режим: APIM (платный, Bearer).
    /// Корневой блок: securities. Одна строка на тикер.
    ///
    /// Содержит только поля, отсутствующие в FuturesSecurityDTO (публичный ISS):
    /// BUYSELLFEE, SCALPERFEE, LASTSETTLEPRICE, IMTIME, SETTLEPRICE_CLR.
    /// 7 из 26 колонок (с SECID и BOARDID для идентификации).
    /// </summary>
    public record MarketStatisticsFuturesSecuritiesDTO
    {
        /// <summary>Код инструмента. MOEX: SECID [0]. Пример: "SiM6".</summary>
        public string? SECID { get; init; }

        /// <summary>Код режима торгов. MOEX: BOARDID [1]. Пример: "RFUD".</summary>
        public string? BOARDID { get; init; }

        /// <summary>Последняя расчётная цена. MOEX: LASTSETTLEPRICE [18]. Пример: 72371.</summary>
        public double? LASTSETTLEPRICE { get; init; }

        /// <summary>Время последнего обновления ГО. MOEX: IMTIME [20]. Формат: "yyyy-MM-dd HH:mm:ss".</summary>
        public string? IMTIME { get; init; }

        /// <summary>Комиссия за сделку (buy/sell). Критично для модели издержек. MOEX: BUYSELLFEE [21]. Пример: 3.34 руб./контракт.</summary>
        public double? BUYSELLFEE { get; init; }

        /// <summary>Комиссия за скальперскую сделку. Критично для модели издержек. MOEX: SCALPERFEE [22]. Пример: 1.67 руб./контракт.</summary>
        public double? SCALPERFEE { get; init; }

        /// <summary>Расчётная цена клиринга. MOEX: SETTLEPRICE_CLR [25]. Пример: 72371.00000.</summary>
        public double? SETTLEPRICE_CLR { get; init; }
    }
}
