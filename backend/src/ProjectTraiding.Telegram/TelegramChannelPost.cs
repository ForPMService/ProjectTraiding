namespace ProjectTraiding.Telegram;

public sealed record TelegramChannelPost(
    long ChannelId,
    string? ChannelUsername,
    string ChannelTitle,
    int MessageId,
    string Text,
    DateTime PublishedAt,
    DateTime ReceivedAt);
