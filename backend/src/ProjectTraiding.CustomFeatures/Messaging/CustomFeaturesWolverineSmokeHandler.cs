namespace ProjectTraiding.CustomFeatures.Messaging;

public sealed class CustomFeaturesWolverineSmokeHandler
{
    public static void Handle(
        CustomFeaturesWolverineSmokeMessage message,
        CustomFeaturesWolverineSmokeAcknowledgement acknowledgement,
        ILogger<CustomFeaturesWolverineSmokeHandler> logger)
    {
        logger.LogInformation(
            "Wolverine smoke message {MessageId} was processed by CustomFeatures.",
            message.MessageId);
        acknowledgement.Confirm(message.MessageId);
    }
}
