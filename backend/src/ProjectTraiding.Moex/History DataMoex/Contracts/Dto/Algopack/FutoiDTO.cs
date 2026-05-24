namespace History_DataMoex.Contracts.Dto.Algopack
{
    public record FutoiDTO
    {
        /// <summary>
        /// Идентификатор торговой сессии.
        ///
        /// MOEX столбец: sess_id
        /// </summary>
        public int? SessId { get; init; }

        /// <summary>
        /// Номер последовательности записи.
        ///
        /// MOEX столбец: seqnum
        /// </summary>
        public int? SeqNum { get; init; }

        /// <summary>
        /// Торговая дата.
        ///
        /// MOEX столбец: tradedate
        /// </summary>
        public string? TradeDate { get; init; }

        /// <summary>
        /// Торговое время.
        ///
        /// MOEX столбец: tradetime
        /// </summary>
        public string? TradeTime { get; init; }

        /// <summary>
        /// Тикер базового инструмента.
        ///
        /// MOEX столбец: ticker
        ///
        /// Пример:
        /// Si.
        /// </summary>
        public string? Ticker { get; init; }

        /// <summary>
        /// Группа клиентов.
        ///
        /// MOEX столбец: clgroup
        ///
        /// Примеры:
        /// FIZ — физические лица,
        /// YUR — юридические лица.
        /// </summary>
        public string? ClGroup { get; init; }

        /// <summary>
        /// Итоговая позиция группы клиентов.
        ///
        /// MOEX столбец: pos
        /// </summary>
        public long? Pos { get; init; }

        /// <summary>
        /// Длинная позиция.
        ///
        /// MOEX столбец: pos_long
        /// </summary>
        public long? PosLong { get; init; }

        /// <summary>
        /// Короткая позиция.
        ///
        /// MOEX столбец: pos_short
        /// </summary>
        public long? PosShort { get; init; }

        /// <summary>
        /// Количество клиентов с длинной позицией.
        ///
        /// MOEX столбец: pos_long_num
        /// </summary>
        public long? PosLongNum { get; init; }

        /// <summary>
        /// Количество клиентов с короткой позицией.
        ///
        /// MOEX столбец: pos_short_num
        /// </summary>
        public long? PosShortNum { get; init; }

        /// <summary>
        /// Системное время формирования записи.
        ///
        /// MOEX столбец: systime
        /// </summary>
        public DateTime? SysTime { get; init; }

        /// <summary>
        /// Дата торговой сессии.
        ///
        /// MOEX столбец: trade_session_date
        /// </summary>
        public string? TradeSessionDate { get; init; }
    }
}