namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Лог-события покрытия приёма реального времени.
    /// </summary>
    public static partial class MoexStreamCoverageLogMessages
    {
        [LoggerMessage(EventId = 240, EventName = "MoexStreamCrashedSessionsClosed", Level = LogLevel.Warning,
            Message = "Crashed stream sessions closed: secid={Secid}, dataKind={DataKind}, count={Count}.")]
        public static partial void CrashedClosed(
            ILogger logger, string secid, string dataKind, int count);

        [LoggerMessage(EventId = 241, EventName = "MoexStreamSessionOpened", Level = LogLevel.Information,
            Message = "Stream session opened: secid={Secid}, dataKind={DataKind}, sessionId={SessionId}.")]
        public static partial void SessionOpened(
            ILogger logger, string secid, string dataKind, long sessionId);

        [LoggerMessage(EventId = 242, EventName = "MoexStreamSessionClosed", Level = LogLevel.Information,
            Message = "Stream session closed: sessionId={SessionId}.")]
        public static partial void SessionClosed(ILogger logger, long sessionId);
    }
}
