namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Лог-события высокой отметки приёма реального времени.
    /// </summary>
    public static partial class MoexStreamCursorLogMessages
    {
        [LoggerMessage(EventId = 230, EventName = "MoexStreamCursorUpserted", Level = LogLevel.Debug,
            Message = "Stream cursor upserted: secid={Secid}, dataKind={DataKind}, lastSourceTime={LastSourceTime}.")]
        public static partial void CursorUpserted(
            ILogger logger, string secid, string dataKind, DateTime lastSourceTime);
    }
}
