namespace ProjectTraiding.Api.Infrastructure;

public static partial class WolverineSmokeLogMessages
{
    [LoggerMessage(
        EventId = 510,
        EventName = "WolverineSmokeMessageConfirmed",
        Level = LogLevel.Information,
        Message = "Wolverine smoke message {MessageId} was confirmed after handler execution.")]
    public static partial void MessageConfirmed(ILogger logger, Guid messageId);

    [LoggerMessage(
        EventId = 511,
        EventName = "WolverineSmokeExecutionFailed",
        Level = LogLevel.Error,
        Message = "Wolverine smoke execution failed.")]
    public static partial void ExecutionFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 512,
        EventName = "WolverineSmokeHostShutdownFailed",
        Level = LogLevel.Error,
        Message = "Wolverine smoke host shutdown failed.")]
    public static partial void HostShutdownFailed(ILogger logger, Exception exception);
}
