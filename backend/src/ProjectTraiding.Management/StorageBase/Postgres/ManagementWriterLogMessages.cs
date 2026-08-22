using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    /// <summary>
    /// Лог-события контура записи Management в PostgreSQL.
    /// EventId 200–209 и 250–259: зарезервировано за management-writer.
    /// Второй диапазон открыт под команды остановки: первый исчерпан полностью.
    /// </summary>
    public static partial class ManagementWriterLogMessages
    {
        // Событие несёт только имя таблицы. Завершение каждой операции пишется
        // отдельным событием этого же класса со строго большим набором сведений.
        [LoggerMessage(
        EventId = 200, EventName = "MgmtWriteStarted", Level = LogLevel.Debug,
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

        [LoggerMessage(
            EventId = 250, EventName = "MgmtRealtimeAllDisabled", Level = LogLevel.Warning,
            Message = "Management realtime all subscriptions disabled: rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeAllDisabled(ILogger logger, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 251, EventName = "MgmtLoadTasksCancelledAll", Level = LogLevel.Warning,
            Message = "Management load tasks cancelled (all): cancelled={CancelledCount}, cancelRequested={CancelRequestedCount}, time={Elapsed}.")]
        public static partial void LoadTasksCancelledAll(
            ILogger logger,
            int cancelledCount,
            int cancelRequestedCount,
            TimeSpan elapsed);

        [LoggerMessage(
            EventId = 252, EventName = "MgmtLoadTasksCancelledInstrument", Level = LogLevel.Warning,
            Message = "Management load tasks cancelled (instrument): secid={Secid}, cancelled={CancelledCount}, cancelRequested={CancelRequestedCount}, time={Elapsed}.")]
        public static partial void LoadTasksCancelledInstrument(
            ILogger logger,
            string secid,
            int cancelledCount,
            int cancelRequestedCount,
            TimeSpan elapsed);

        [LoggerMessage(
            EventId = 253, EventName = "MgmtInstrumentPostgresDataDeleted", Level = LogLevel.Warning,
            Message = "Management instrument postgres data deleted: secid={Secid}, ranges={RangesDeleted}, cursors={CursorsDeleted}, tasks={TasksDeleted}, time={Elapsed}.")]
        public static partial void InstrumentPostgresDataDeleted(
            ILogger logger,
            string secid,
            int rangesDeleted,
            int cursorsDeleted,
            int tasksDeleted,
            TimeSpan elapsed);

        [LoggerMessage(
            EventId = 254, EventName = "MgmtInstrumentClickHouseTableCleared", Level = LogLevel.Information,
            Message = "Management instrument clickhouse table cleared: secid={Secid}, table={Table}.")]
        public static partial void InstrumentClickHouseTableCleared(
            ILogger logger, string secid, string table);

        [LoggerMessage(
            EventId = 255, EventName = "MgmtInstrumentClickHouseDataDeleted", Level = LogLevel.Warning,
            Message = "Management instrument clickhouse data deleted: secid={Secid}, tables={TablesCleared}, time={Elapsed}.")]
        public static partial void InstrumentClickHouseDataDeleted(
            ILogger logger, string secid, int tablesCleared, TimeSpan elapsed);

    }
}
