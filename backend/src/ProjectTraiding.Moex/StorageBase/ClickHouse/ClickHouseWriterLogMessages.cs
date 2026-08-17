using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>EventId 180–189: writer-контур ClickHouse.</summary>
    public static partial class ClickHouseWriterLogMessages
    {
        [LoggerMessage(
            EventId = 180, EventName = "ClickHouseWriteStarted", Level = LogLevel.Information,
            Message = "ClickHouse write started: table={Table}, rows={RowCount}.")]
        public static partial void WriteStarted(ILogger logger, string table, int rowCount);

        [LoggerMessage(
            EventId = 182, EventName = "ClickHouseWriteFailed", Level = LogLevel.Error,
            Message = "ClickHouse write failed: table={Table}, errorType={ErrorType}.")]
        public static partial void WriteFailed(ILogger logger, Exception exception, string table, string errorType);

        [LoggerMessage(
            EventId = 183, EventName = "ClickHouseRangeWritten", Level = LogLevel.Information,
            Message = "ClickHouse range written: secid={Secid}, rowsRead={RowsRead}, rowsInsertedReported={RowsInsertedReported}.")]
        public static partial void RangeWritten(ILogger logger, string secid, long rowsRead, long rowsInsertedReported);
    }
}
