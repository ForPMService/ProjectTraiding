using Microsoft.Extensions.Logging;

namespace ProjectTraiding.Europe.Telegram;

internal static partial class TelegramLog
{
    [LoggerMessage(1, LogLevel.Information, "Receiver state: {State}.")]
    internal static partial void State(ILogger logger, TelegramReceiverState state);

    [LoggerMessage(2, LogLevel.Error, "Telegram operation {Operation} failed: {Category}.")]
    internal static partial void Failure(ILogger logger, string operation, TelegramErrorCategory category);

    [LoggerMessage(3, LogLevel.Error, "Publication {ChannelId}/{MessageId} failed: {Category}.")]
    internal static partial void PublicationFailure(ILogger logger, long channelId, int messageId, TelegramErrorCategory category);

    [LoggerMessage(4, LogLevel.Warning, "Publication {ChannelId}/{MessageId} discarded: invalid UTC timestamp.")]
    internal static partial void InvalidTime(ILogger logger, long channelId, int messageId);

    [LoggerMessage(5, LogLevel.Information, "Owner bot state: {State}.")]
    internal static partial void BotState(ILogger logger, string state);
}
