using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    /// <summary>
    /// Лог-события контура чтения Vitrine из PostgreSQL.
    /// EventId 300–309: зарезервировано за vitrine-read.
    /// </summary>
    public static partial class VitrineReadLogMessages
    {
        [LoggerMessage(
        EventId = 300, EventName = "VitrineReadStarted", Level = LogLevel.Information,
        Message = "Vitrine DB read started: table={Table}.")]
        public static partial void ReadStarted(ILogger logger, string table);

        [LoggerMessage(
            EventId = 301, EventName = "VitrineReadCompleted", Level = LogLevel.Information,
            Message = "Vitrine DB read completed: table={Table}, rows={RowsRead}, time={ElapsedMs}.")]
        public static partial void ReadCompleted(ILogger logger, string table, int rowsRead, TimeSpan elapsedMs);

        [LoggerMessage(
            EventId = 302, EventName = "VitrineReadFailed", Level = LogLevel.Error,
            Message = "Vitrine DB read failed: table={Table}, errorType={ErrorType}.")]
        public static partial void ReadFailed(ILogger logger, Exception exception, string table, string errorType);
    }
}
