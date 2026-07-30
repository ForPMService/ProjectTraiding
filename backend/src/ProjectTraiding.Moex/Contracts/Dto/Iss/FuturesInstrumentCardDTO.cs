namespace ProjectTraiding.Moex.Contracts.Dto.Iss
{
    /// <summary>
    /// Карточка фьючерса — статика + текущая цена из одного списочного ответа.
    ///
    /// Источник: GET /engines/futures/markets/forts/boards/RFUD/securities.json?iss.meta=off
    /// Режим: APIM (платный, Bearer). ISS отдаёт пустые BID/OFFER — подтверждено пробником.
    /// Блоки: securities (паспорт) + marketdata (цена/объёмы).
    ///
    /// Карточка конкретного инструмента = строка из списочного ответа по SECID.
    /// Отдельный пер-тикерный вызов не нужен (решение из MOEX_Cards_Handoff_v0_1).
    ///
    /// Probe: probe-14, 2026-06-01. Securities 16 из 26 колонок, marketdata 14 из 37.
    /// </summary>
    public record FuturesInstrumentCardDTO
    {
        // ── securities (16 полей) ──────────────────────────────

        /// <summary>Код инструмента. MOEX: SECID [0]. Пример: "SiM6".</summary>
        public string? SecId { get; init; }

        /// <summary>Код режима торгов. MOEX: BOARDID [1]. Пример: "RFUD".</summary>
        public string? BoardId { get; init; }

        /// <summary>Краткое название. MOEX: SHORTNAME [2]. Пример: "Si-6.26".</summary>
        public string? ShortName { get; init; }

        /// <summary>Полное название. MOEX: SECNAME [3].</summary>
        public string? SecName { get; init; }

        /// <summary>Количество знаков после запятой. MOEX: DECIMALS [5]. Пример: 0.</summary>
        public int? Decimals { get; init; }

        /// <summary>Минимальный шаг цены. MOEX: MINSTEP [6]. Пример: 1.00000.</summary>
        public double? MinStep { get; init; }

        /// <summary>Последний день торгов. MOEX: LASTTRADEDATE [7]. Пример: "2026-06-18".</summary>
        public string? LastTradeDate { get; init; }

        /// <summary>Дата исполнения. MOEX: LASTDELDATE [8]. Пример: "2026-06-18".</summary>
        public string? LastDelDate { get; init; }

        /// <summary>
        /// Код серии контракта. MOEX: SECTYPE [9].
        /// Примеры: "SR" у SRU6, "Si" у SiU6.
        /// Используется как ключ запроса открытого интереса.
        /// Не равен коду базового актива: у SRU6 SECTYPE = "SR",
        /// тогда как ASSETCODE = "SBRF".
        /// </summary>
        public string? SecType { get; init; }

        /// <summary>Код базового актива. MOEX: ASSETCODE [11]. Пример: "Si".</summary>
        public string? AssetCode { get; init; }

        /// <summary>Размер лота. MOEX: LOTVOLUME [13]. Пример: 1.</summary>
        public int? LotVolume { get; init; }

        /// <summary>Гарантийное обеспечение. MOEX: INITIALMARGIN [14]. Пример: 7741.57.</summary>
        public double? InitialMargin { get; init; }

        /// <summary>Верхний лимит цены. MOEX: HIGHLIMIT [15].</summary>
        public double? HighLimit { get; init; }

        /// <summary>Нижний лимит цены. MOEX: LOWLIMIT [16].</summary>
        public double? LowLimit { get; init; }

        /// <summary>Стоимость шага цены. MOEX: STEPPRICE [17]. Пример: 1.00000.</summary>
        public double? StepPrice { get; init; }

        /// <summary>Комиссия за сделку. MOEX: BUYSELLFEE [21]. Пример: 3.34.</summary>
        public double? BuySellFee { get; init; }

        // ── marketdata (14 полей) ──────────────────────────────

        /// <summary>Лучшая цена покупки. MOEX: BID [2].</summary>
        public double? Bid { get; init; }

        /// <summary>Лучшая цена продажи. MOEX: OFFER [3].</summary>
        public double? Offer { get; init; }

        /// <summary>Спред. MOEX: SPREAD [4].</summary>
        public double? Spread { get; init; }

        /// <summary>Цена открытия. MOEX: OPEN [5].</summary>
        public double? Open { get; init; }

        /// <summary>Максимальная цена. MOEX: HIGH [6].</summary>
        public double? High { get; init; }

        /// <summary>Минимальная цена. MOEX: LOW [7].</summary>
        public double? Low { get; init; }

        /// <summary>Последняя цена. MOEX: LAST [8].</summary>
        public double? Last { get; init; }

        /// <summary>Расчётная цена. MOEX: SETTLEPRICE [11].</summary>
        public double? SettlePrice { get; init; }

        /// <summary>Открытые позиции. MOEX: OPENPOSITION [13].</summary>
        public double? OpenPosition { get; init; }

        /// <summary>Количество сделок. MOEX: NUMTRADES [14].</summary>
        public int? NumTrades { get; init; }

        /// <summary>Объём в контрактах. MOEX: VOLTODAY [15].</summary>
        public long? VolToday { get; init; }

        /// <summary>Объём в валюте рынка. MOEX: VALTODAY [16].</summary>
        public long? ValToday { get; init; }

        /// <summary>Время обновления. MOEX: UPDATETIME [18]. Пример: "14:06:21".</summary>
        public string? UpdateTime { get; init; }

        /// <summary>Изменение открытых позиций. MOEX: OICHANGE [32].</summary>
        public long? OiChange { get; init; }
    }
}
