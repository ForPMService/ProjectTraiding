using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>EventId 190–199: жизненный цикл задачи загрузки и учёт диапазона.</summary>
    public static partial class MoexLoadTaskLogMessages
    {
        [LoggerMessage(
            EventId = 190, EventName = "LoadTaskRunning", Level = LogLevel.Information,
            Message = "Load task taken to work: id={TaskId}, time={Elapsed}.")]
        public static partial void TaskRunning(ILogger logger, Guid taskId, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 191, EventName = "LoadTaskDone", Level = LogLevel.Information,
            Message = "Load task done: id={TaskId}, rows={RowsLoaded}, time={Elapsed}.")]
        public static partial void TaskDone(ILogger logger, Guid taskId, long rowsLoaded, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 192, EventName = "LoadTaskError", Level = LogLevel.Error,
            Message = "Load task failed: id={TaskId}, time={Elapsed}.")]
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
    }
}
