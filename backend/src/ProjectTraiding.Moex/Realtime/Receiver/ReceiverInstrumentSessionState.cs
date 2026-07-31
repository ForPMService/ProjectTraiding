namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Общее состояние сеанса приёма по одному инструменту: то, что одинаково
    /// у сделок, стакана и свечей. Предметные дополнения живут в наследниках.
    ///
    /// Класс не абстрактный намеренно: стакану достаточно ровно этих членов,
    /// и пустой наследник ради одинакового вида имён не заводится. Если у стакана
    /// появится собственное состояние, наследник будет введён тогда.
    /// </summary>
    internal class ReceiverInstrumentSessionState
    {
        public ReceiverInstrumentSessionState(
            long sessionId,
            string market,
            string boardId,
            long lastHeartbeatTimestamp)
        {
            SessionId = sessionId;
            Market = market;
            BoardId = boardId;
            LastHeartbeatTimestamp = lastHeartbeatTimestamp;
        }

        public long SessionId { get; }
        public string Market { get; }
        public string BoardId { get; }
        public long RowsTotal { get; set; }
        public long LastHeartbeatTimestamp { get; set; }

        /// <summary>
        /// Подписка снята оператором: инструмент исключён из опроса, сеанс покрытия ждёт
        /// штатного закрытия. Состояние удаляется из словаря только после успешного закрытия.
        /// Обратно в активное не возвращается — иначе сеанс перекрыл бы период отключения.
        /// </summary>
        public bool IsStopping { get; set; }
    }
}
