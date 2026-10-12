namespace ProjectTraiding.Europe.Telegram;

/// <summary>A publication accepted by the adapter. Both timestamps represent UTC instants.</summary>
public sealed record TelegramChannelPost(long ChannelId, string? ChannelUsername, string ChannelTitle,
    int MessageId, string Text, DateTimeOffset PublishedAt, DateTimeOffset ReceivedAt);
