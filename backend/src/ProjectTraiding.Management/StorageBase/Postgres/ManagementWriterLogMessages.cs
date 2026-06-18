using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    /// <summary>
    /// Лог-события контура записи Management в PostgreSQL.
    /// EventId 200–209: зарезервировано за management-writer.
    /// </summary>
    public static partial class ManagementWriterLogMessages
    {
        [LoggerMessage(
        EventId = 200, EventName = "MgmtWriteStarted", Level = LogLevel.Information,
        Message = "Management DB write started: table={Table}.")]
        public static partial void WriteStarted(ILogger logger, string table);

        [LoggerMessage(
            EventId = 201, EventName = "MgmtWriteCompleted", Level = LogLevel.Information,
            Message = "Management DB write completed: table={Table}, id={Id}, rows={RowsWritten}, time={ElapsedMs}.")]
        public static partial void WriteCompleted(ILogger logger, string table, long id, int rowsWritten, TimeSpan elapsedMs);

        [LoggerMessage(
            EventId = 202, EventName = "MgmtWriteRolledBack", Level = LogLevel.Error,
            Message = "Management DB write rolled back: table={Table}, errorType={ErrorType}.")]
        public static partial void WriteRolledBack(ILogger logger, Exception exception, string table, string errorType);
    }
}
