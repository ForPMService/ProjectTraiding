using Microsoft.Extensions.Logging;
using TL;

namespace ProjectTraiding.Europe.Telegram;

internal static class TelegramUpdateHandler
{
    internal sealed record ChannelInfo(string Username, string Title);

    internal static TelegramChannelPost? Convert(Update update, Dictionary<long, ChannelInfo> channels,
        DateTimeOffset readyAtUtc, DateTimeOffset receivedAtUtc, ILogger logger)
    {
        // UpdateNewChannelMessage derives from UpdateNewMessage in this version of TL.
        if (update is not UpdateNewMessage { message: Message message }
            || message.peer_id is not PeerChannel peer || message.id <= 0
            || !channels.TryGetValue(peer.channel_id, out ChannelInfo? channel)
            || string.IsNullOrWhiteSpace(message.message)) return null;
        if (message.date.Kind != DateTimeKind.Utc)
        {
            TelegramLog.InvalidTime(logger, peer.channel_id, message.id);
            return null;
        }
        DateTimeOffset publishedAtUtc = new(message.date);
        if (publishedAtUtc <= readyAtUtc) return null;
        return new(peer.channel_id, channel.Username, channel.Title,
            message.id, message.message, publishedAtUtc, receivedAtUtc);
    }
}
