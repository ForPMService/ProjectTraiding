using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TL;

namespace ProjectTraiding.Telegram;

public sealed class TelegramUpdateHandler(ILogger<TelegramUpdateHandler> logger, IHostEnvironment environment)
{
    internal readonly record struct ChannelInfo(string? Username, string Title, bool Broadcast);

    internal void Handle(Update update, Dictionary<long, ChannelInfo> channels, DateTime readyAtUtc)
    {
        if (update is not UpdateNewMessage { message: Message message }
            || message.peer_id is not PeerChannel peer
            || !channels.TryGetValue(peer.channel_id, out ChannelInfo channel)
            || !channel.Broadcast
            || string.IsNullOrEmpty(message.message)) return;

        // TL.ReadTLStamp в WTelegramClient 4.4.9 декодирует Unix seconds с DateTimeKind.Utc.
        // Не приписываем UTC неожиданному Unspecified: такой update пропускаем.
        if (message.date.Kind != DateTimeKind.Utc)
        {
            logger.LogWarning("Telegram: публикация {ChannelId}/{MessageId} пропущена: PublishedAt.Kind={PublishedAtKind}.",
                peer.channel_id, message.id, message.date.Kind);
            return;
        }
        // Секундная точность Telegram: вся секунда готовности также исключается.
        if (message.date <= readyAtUtc) return;

        TelegramChannelPost post = new(peer.channel_id, channel.Username, channel.Title,
            message.id, message.message, message.date, DateTime.UtcNow);
        if (environment.IsDevelopment())
        {
            logger.LogInformation("Telegram: ChannelId={ChannelId}, ChannelUsername={ChannelUsername}, ChannelTitle={ChannelTitle}, "
                + "MessageId={MessageId}, PublishedAt={PublishedAt:O}, PublishedAt.Kind={PublishedAtKind}, "
                + "ReceivedAt={ReceivedAt:O}, ReceivedAt.Kind={ReceivedAtKind}, Text={Text}",
                post.ChannelId, post.ChannelUsername, post.ChannelTitle, post.MessageId,
                post.PublishedAt, post.PublishedAt.Kind, post.ReceivedAt, post.ReceivedAt.Kind, post.Text);
        }
        else
        {
            logger.LogInformation("Telegram: ChannelId={ChannelId}, ChannelUsername={ChannelUsername}, ChannelTitle={ChannelTitle}, "
                + "MessageId={MessageId}, PublishedAt={PublishedAt:O}, PublishedAt.Kind={PublishedAtKind}, "
                + "ReceivedAt={ReceivedAt:O}, ReceivedAt.Kind={ReceivedAtKind}",
                post.ChannelId, post.ChannelUsername, post.ChannelTitle, post.MessageId,
                post.PublishedAt, post.PublishedAt.Kind, post.ReceivedAt, post.ReceivedAt.Kind);
        }
    }
}
