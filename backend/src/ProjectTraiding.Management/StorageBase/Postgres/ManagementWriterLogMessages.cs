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

        [LoggerMessage(
            EventId = 203, EventName = "MgmtRealtimeTradesEnabled", Level = LogLevel.Information,
            Message = "Management realtime trades enabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeTradesEnabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 204, EventName = "MgmtRealtimeTradesDisabled", Level = LogLevel.Information,
            Message = "Management realtime trades disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeTradesDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 205, EventName = "MgmtRealtimeOrderbookEnabled", Level = LogLevel.Information,
            Message = "Management realtime orderbook enabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeOrderbookEnabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 206, EventName = "MgmtRealtimeOrderbookDisabled", Level = LogLevel.Information,
            Message = "Management realtime orderbook disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeOrderbookDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 207, EventName = "MgmtRealtimeCandlesEnabled", Level = LogLevel.Information,
            Message = "Management realtime candles enabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeCandlesEnabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 208, EventName = "MgmtRealtimeCandlesDisabled", Level = LogLevel.Information,
            Message = "Management realtime candles disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeCandlesDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 209, EventName = "MgmtRealtimeInstrumentDisabled", Level = LogLevel.Information,
            Message = "Management realtime instrument disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeInstrumentDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);
    }
}
