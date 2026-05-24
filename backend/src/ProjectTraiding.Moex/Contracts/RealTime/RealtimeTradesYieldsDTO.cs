namespace History_DataMoex.Contracts.Dto.Realtime
{
    /// <summary>
    /// Строка блока доходности сделок MOEX real-time.
    /// 
    /// Root key: "trades_yields"
    /// Колонок: 2
    /// 
    /// В текущих raw samples блок приходит с columns, но без data.
    /// DTO нужен, чтобы parser фиксировал наличие блока и не падал,
    /// если MOEX начнёт наполнять data.
    /// </summary>
    public record RealtimeTradesYieldsDTO
    {
        /// <summary>
        /// Идентификатор режима торгов.
        /// 
        /// MOEX столбец: boardid
        /// MOEX тип: string
        /// </summary>
        public string? BoardId { get; init; }

        /// <summary>
        /// Код инструмента.
        /// 
        /// MOEX столбец: secid
        /// MOEX тип: string
        /// </summary>
        public string? SecId { get; init; }
    }
}
