namespace ProjectTraiding.Moex.Contracts.Dto.Realtime
{
    /// <summary>
    /// Версия данных MOEX — служебный блок, вложенный в ответы orderbook и trades.
    /// 
    /// Root key: "dataversion"
    /// Колонок: 4
    /// Всегда одна строка в data[].
    /// 
    /// Назначение:
    /// — определить, изменились ли данные между двумя запросами (по seqnum);
    /// — подтвердить торговую дату;
    /// — сравнить data_version между последовательными запросами одного и того же endpoint-а.
    /// 
    /// Структура одинакова для stock и futures.
    /// </summary>
    public record RealtimeDataVersionDTO
    {
        /// <summary>
        /// Версия данных, которую MOEX отдаёт в служебном блоке dataversion.
        /// Предполагаемый индикатор изменения данных между запросами.
        /// Монотонность проверяется диагностическим циклом REST-опроса.
        /// 
        /// MOEX столбец: data_version
        /// MOEX тип: int32
        /// 
        /// Пример: 8895 (stock SBER), 13038 (futures SVM6).
        /// </summary>
        public int? DataVersion { get; init; }

        /// <summary>
        /// Порядковый номер последнего обновления.
        /// 
        /// MOEX столбец: seqnum
        /// MOEX тип: int64
        /// 
        /// Формат: YYYYMMDDHHmmss (как число).
        /// Пример: 20260521203511.
        /// 
        /// Используется для определения свежести данных при диагностическом опросе.
        /// </summary>
        public long? SeqNum { get; init; }

        /// <summary>
        /// Торговая дата.
        /// 
        /// MOEX столбец: trade_date
        /// MOEX тип: date (bytes: 10)
        /// 
        /// Пример: "2026-05-21"
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Дата торговой сессии.
        /// 
        /// MOEX столбец: trade_session_date
        /// MOEX тип: date (bytes: 10)
        /// 
        /// Обычно совпадает с TradeDate.
        /// Может отличаться в вечернюю/ночную сессию.
        /// </summary>
        public string? TradeSessionDate { get; init; }
    }
}
