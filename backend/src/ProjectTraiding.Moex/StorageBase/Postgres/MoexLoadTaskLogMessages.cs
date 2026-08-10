using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>EventId 190–200 и 220–229: жизненный цикл задачи загрузки и учёт диапазона.</summary>
    public static partial class MoexLoadTaskLogMessages
    {
        [LoggerMessage(
            EventId = 190, EventName = "LoadTaskRunning", Level = LogLevel.Debug,
            Message = "Load task status set to running: id={TaskId}, sqlTime={Elapsed}.")]
        public static partial void TaskRunning(ILogger logger, Guid taskId, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 191, EventName = "LoadTaskDone", Level = LogLevel.Debug,
            Message = "Load task status set to done: id={TaskId}, rows={RowsLoaded}, sqlTime={Elapsed}.")]
        public static partial void TaskDone(ILogger logger, Guid taskId, long rowsLoaded, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 192, EventName = "LoadTaskError", Level = LogLevel.Debug,
            Message = "Load task status set to error: id={TaskId}, sqlTime={Elapsed}.")]
        public static partial void TaskError(ILogger logger, Guid taskId, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 193, EventName = "LoadTaskClaimMissed", Level = LogLevel.Information,
            Message = "Load task claim missed (already taken or absent): id={TaskId}.")]
        public static partial void TaskClaimMissed(ILogger logger, Guid taskId);

        [LoggerMessage(
            EventId = 194, EventName = "LoadedRangeRecorded", Level = LogLevel.Information,
            Message = "Loaded range recorded: secid={Secid}, kind={DataKind}, rows={RowsTotal}, time={Elapsed}.")]
        public static partial void RangeRecorded(ILogger logger, string secid, string dataKind, long rowsTotal, TimeSpan elapsed);

        [LoggerMessage(
        EventId = 195, EventName = "LoadBackgroundStarted", Level = LogLevel.Information,
        Message = "Load background service started: pollInterval={PollInterval}.")]
        public static partial void BackgroundStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(
            EventId = 196, EventName = "LoadBackgroundStopped", Level = LogLevel.Information,
            Message = "Load background service stopped.")]
        public static partial void BackgroundStopped(ILogger logger);

        [LoggerMessage(
            EventId = 197, EventName = "LoadBackgroundPollFailed", Level = LogLevel.Error,
            Message = "Load background poll failed unexpectedly.")]
        public static partial void BackgroundPollFailed(ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 198, EventName = "LoadBackgroundTaskFailed", Level = LogLevel.Warning,
            Message = "Load background task failed (marked error by runner): id={TaskId}.")]
        public static partial void BackgroundTaskFailed(ILogger logger, Guid taskId);

        /// <summary>
        /// Итог исторического задания: одна попытка записи на одно задание, для которого
        /// успешно прочитана строка, определены метки и начат наблюдаемый жизненный цикл.
        /// Длительность здесь — время задания целиком, а не время команды изменения статуса:
        /// именно этим событие отличается от технических событий смены статуса ниже.
        ///
        /// Идентификатор задачи в журнале допустим: ограничение кардинальности относится
        /// к метрикам, а не к записям журнала и трассам.
        /// </summary>
        [LoggerMessage(
            EventId = 200,
            EventName = "LoadTaskCompleted",
            Message = "Load task completed: id={TaskId}, dataKind={DataKind}, market={Market}, " +
                      "outcome={Outcome}, rows={Rows}, duration={Duration}, " +
                      "stopReason={StopReason}, errorType={ErrorType}.")]
        public static partial void TaskCompleted(
            ILogger logger,
            LogLevel level,
            Guid taskId,
            string dataKind,
            string market,
            string outcome,
            long rows,
            TimeSpan duration,
            string? stopReason,
            string? errorType);

        [LoggerMessage(EventId = 220, EventName = "MoexLoadTaskRequeuedAfterCancel",
           Level = LogLevel.Information,
           Message = "Load task requeued after cancel: taskId={TaskId}, affected={Affected}, elapsed={Elapsed}.")]
        public static partial void TaskRequeuedAfterCancel(ILogger logger, Guid taskId, int affected, TimeSpan elapsed);

        [LoggerMessage(EventId = 221, EventName = "MoexLoadTaskCancelWatchPollFailed",
           Level = LogLevel.Warning,
           Message = "Load task cancellation watch poll failed: taskId={TaskId}, errorType={ErrorType}.")]
        public static partial void CancelWatchPollFailed(
            ILogger logger,
            Exception exception,
            Guid taskId,
            string errorType);
    }
}
