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
    public class ReceiverInstrumentSessionState
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
        /// Время последнего успешного опроса этого инструмента. Отвечает на вопрос
        /// «жив ли приём по инструменту», а не «свежи ли данные»: успешным считается
        /// и пустой ответ, потому что отсутствие сделок за интервал — нормальное
        /// состояние малоликвидного инструмента, а не отказ.
        /// </summary>
        public DateTimeOffset? LastSuccessfulPollTime { get; set; }

        /// <summary>
        /// Время последних рыночных данных, фактически записанных в постоянное хранилище
        /// по этому инструменту. Отвечает на вопрос «насколько свежи данные».
        /// Пустой успешный опрос это время не двигает: записывать было нечего.
        ///
        /// Значение берётся из самих данных источника, а не из часов приёмника:
        /// отставание источника обязано быть видно, а не замаскировано нашим временем.
        /// </summary>
        public DateTimeOffset? LastConfirmedMarketTime { get; set; }

        /// <summary>
        /// Подписка снята оператором: инструмент исключён из опроса, сеанс покрытия ждёт
        /// штатного закрытия. Состояние удаляется из словаря только после успешного закрытия.
        /// Обратно в активное не возвращается — иначе сеанс перекрыл бы период отключения.
        /// </summary>
        public bool IsStopping { get; set; }
    }
}
