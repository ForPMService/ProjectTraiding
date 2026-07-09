using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Endpoints
{
    /// <summary>
    /// Лог-события endpoint-контура Management.
    /// EventId 210–219: зарезервировано за operator-endpoint.
    /// </summary>
    public static partial class ManagementEndpointLogMessages
    {
        [LoggerMessage(
        EventId = 210, EventName = "MgmtOperationStarted", Level = LogLevel.Information,
        Message = "Management operation started: route={Route}.")]
        public static partial void OperationStarted(ILogger logger, string route);

        [LoggerMessage(
            EventId = 211, EventName = "MgmtValidationRejected", Level = LogLevel.Warning,
            Message = "Management input rejected: route={Route}, errors={Errors}.")]
        public static partial void ValidationRejected(ILogger logger, string route, string errors);

        [LoggerMessage(
            EventId = 212, EventName = "MgmtDbErrorMapped", Level = LogLevel.Warning,
            Message = "Management DB error mapped to operator text: route={Route}, sqlState={SqlState}.")]
        public static partial void DbErrorMapped(ILogger logger, string route, string sqlState);

        [LoggerMessage(
            EventId = 213, EventName = "MgmtBulkLoadTasksCreated", Level = LogLevel.Information,
            Message = "Management bulk load tasks created: route={Route}, expanded={ExpandedCount}, inserted={InsertedCount}, skippedDuplicates={SkippedDuplicateCount}.")]
        public static partial void BulkLoadTasksCreated(
            ILogger logger,
            string route,
            int expandedCount,
            int insertedCount,
            int skippedDuplicateCount);

        [LoggerMessage(
            EventId = 214, EventName = "MgmtBulkFillMissingResolved", Level = LogLevel.Information,
            Message = "Management bulk fill missing resolved: route={Route}, secid={Secid}, dataKind={DataKind}, candleInterval={CandleInterval}, requestedFrom={RequestedFrom}, requestedTill={RequestedTill}, effectiveFrom={EffectiveFrom}, effectiveTill={EffectiveTill}, coveredIntervals={CoveredIntervalsCount}, missingIntervals={MissingIntervalsCount}, missingDays={MissingDaysTotal}, windowsCreated={WindowsCreatedCount}.")]
        public static partial void BulkFillMissingResolved(
            ILogger logger,
            string route,
            string secid,
            string dataKind,
            int? candleInterval,
            DateOnly requestedFrom,
            DateOnly requestedTill,
            DateOnly effectiveFrom,
            DateOnly effectiveTill,
            int coveredIntervalsCount,
            int missingIntervalsCount,
            int missingDaysTotal,
            int windowsCreatedCount);
    }
}
