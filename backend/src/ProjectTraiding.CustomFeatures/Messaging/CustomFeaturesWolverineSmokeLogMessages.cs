namespace ProjectTraiding.CustomFeatures.Messaging;

public static partial class CustomFeaturesWolverineSmokeLogMessages
{
    [LoggerMessage(
        EventId = 320,
        EventName = "CustomFeaturesWolverineSmokeMessageProcessed",
        Level = LogLevel.Information,
        Message = "Wolverine smoke message {MessageId} was processed by CustomFeatures.")]
    public static partial void MessageProcessed(ILogger logger, Guid messageId);
}
