namespace ProjectTraiding.CustomFeatures.Messaging;

public sealed class CustomFeaturesWolverineSmokeHandler
{
    public static void Handle(
        CustomFeaturesWolverineSmokeMessage message,
        CustomFeaturesWolverineSmokeAcknowledgement acknowledgement,
        ILogger<CustomFeaturesWolverineSmokeHandler> logger)
    {
        CustomFeaturesWolverineSmokeLogMessages.MessageProcessed(logger, message.MessageId);
        acknowledgement.Confirm(message.MessageId);
    }
}
