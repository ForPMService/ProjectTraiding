using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Storage.Postgres
{
    /// <summary>
    /// Лог-события контура записи в PostgreSQL.
    /// EventId 140–149: зарезервировано за writer-контур.
    /// </summary>
    public static partial class MoexWriterLogMessages
    {
        [LoggerMessage(
           EventId = 140, EventName = "MoexWriteStarted", Level = LogLevel.Information,
           Message = "DB write started: table={Table}, input={InputCount}.")]
        public static partial void WriteStarted(ILogger logger, string table, int inputCount);

        [LoggerMessage(
            EventId = 141, EventName = "MoexWriteCompleted", Level = LogLevel.Information,
            Message = "DB write completed: table={Table}, rows={RowsWritten}, time={ElapsedMs}.")]
        public static partial void WriteCompleted(ILogger logger, string table, int rowsWritten, TimeSpan elapsedMs);

        [LoggerMessage(
            EventId = 142, EventName = "MoexWriteRolledBack", Level = LogLevel.Error,
            Message = "DB write rolled back: table={Table}, atKey={Key}, processed={Processed}, errorType={ErrorType}.")]
        public static partial void WriteRolledBack(ILogger logger, Exception exception, string table, string key, int processed, string errorType);

        [LoggerMessage(
            EventId = 143, EventName = "MoexWriteDateParseFailed", Level = LogLevel.Warning,
            Message = "Date parse failed, NULL written: table={Table}, field={Field}, key={Key}, rawValue={RawValue}.")]
        public static partial void DateParseFailed(ILogger logger, string table, string field, string key, string rawValue);
    }
}
