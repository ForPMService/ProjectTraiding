using TL;

namespace ProjectTraiding.Europe.Telegram;

internal static class TelegramUpdateHandler
{
    internal static TelegramChannelPost? Convert(Update update, Dictionary<long, Channel> channels)
    {
        // UpdateNewChannelMessage derives from UpdateNewMessage; edits have different update types.
        if (update is not UpdateNewMessage { message: Message message }
            || message.peer_id is not PeerChannel peer
            || !channels.TryGetValue(peer.channel_id, out Channel? channel))
            return null;

        // TL timestamps are Unix seconds decoded as UTC. No readiness or content filter is applied.
        DateTimeOffset publishedAtUtc = new(message.date.Ticks, TimeSpan.Zero);
        return new(channel.id, channel.username, channel.title, message.id, message.message,
            publishedAtUtc, DateTimeOffset.UtcNow);
    }
}
