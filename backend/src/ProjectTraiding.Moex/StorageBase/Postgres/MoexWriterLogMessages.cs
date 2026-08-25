using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Лог-события контура записи в PostgreSQL.
    /// EventId 170–179: зарезервировано за writer-контур PostgreSQL.
    /// </summary>
    public static partial class MoexWriterLogMessages
    {
        // Событие сменяется парным MoexWriteCompleted (EventId 171) со строго большим
        // набором сведений. Уровень понижен по прецеденту ClickHouseWriteStarted (180).
        [LoggerMessage(
           EventId = 170, EventName = "MoexWriteStarted", Level = LogLevel.Debug,
           Message = "DB write started: table={Table}, input={InputCount}.")]
        public static partial void WriteStarted(ILogger logger, string table, int inputCount);

        [LoggerMessage(
            EventId = 171, EventName = "MoexWriteCompleted", Level = LogLevel.Information,
            Message = "DB write completed: table={Table}, rows={RowsWritten}, time={ElapsedMs}.")]
        public static partial void WriteCompleted(ILogger logger, string table, int rowsWritten, TimeSpan elapsedMs);

        [LoggerMessage(
            EventId = 172, EventName = "MoexWriteRolledBack", Level = LogLevel.Error,
            Message = "DB write rolled back: table={Table}, atKey={Key}, processed={Processed}, errorType={ErrorType}.")]
        public static partial void WriteRolledBack(ILogger logger, Exception exception, string table, string key, int processed, string errorType);

        [LoggerMessage(
            EventId = 173, EventName = "MoexWriteDateParseFailed", Level = LogLevel.Warning,
            Message = "Date parse failed, NULL written: table={Table}, field={Field}, key={Key}, rawValue={RawValue}.")]
        public static partial void DateParseFailed(ILogger logger, string table, string field, string key, string rawValue);

        [LoggerMessage(
            EventId = 250, EventName = "MoexInstrumentPostgresDataDeleted", Level = LogLevel.Warning,
            Message = "Moex instrument postgres data deleted: secid={Secid}, ranges={RangesDeleted}, cursors={CursorsDeleted}, tasks={TasksDeleted}, time={Elapsed}.")]
        public static partial void InstrumentPostgresDataDeleted(
            ILogger logger,
            string secid,
            int rangesDeleted,
            int cursorsDeleted,
            int tasksDeleted,
            TimeSpan elapsed);

        [LoggerMessage(
            EventId = 251, EventName = "MoexInstrumentClickHouseTableCleared", Level = LogLevel.Information,
            Message = "Moex instrument clickhouse table cleared: secid={Secid}, table={Table}.")]
        public static partial void InstrumentClickHouseTableCleared(
            ILogger logger, string secid, string table);

        [LoggerMessage(
            EventId = 252, EventName = "MoexInstrumentClickHouseDataDeleted", Level = LogLevel.Warning,
            Message = "Moex instrument clickhouse data deleted: secid={Secid}, tables={TablesCleared}, time={Elapsed}.")]
        public static partial void InstrumentClickHouseDataDeleted(
            ILogger logger, string secid, int tablesCleared, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 253, EventName = "MoexHistoryCoverageRewritten", Level = LogLevel.Warning,
            Message = "Moex history coverage rewritten: secid={Secid}, dataKind={DataKind}, rows={Affected}.")]
        public static partial void HistoryCoverageRewritten(
            ILogger logger, string secid, string dataKind, int affected);

        [LoggerMessage(
            EventId = 254, EventName = "MoexSeriesRangeDeleted", Level = LogLevel.Warning,
            Message = "Moex series range deleted: secid={Secid}, table={Table}, from={From}, till={Till}, time={Elapsed}.")]
        public static partial void SeriesRangeDeleted(
            ILogger logger, string secid, string table, string from, string till, TimeSpan elapsed);
    }
}
