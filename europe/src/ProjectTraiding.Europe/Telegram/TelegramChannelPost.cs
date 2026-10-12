namespace ProjectTraiding.Europe.Telegram;

/// <summary>Неизменяемые данные принятой публикации; оба момента времени указаны в UTC.</summary>
public sealed record TelegramChannelPost(long ChannelId, string? ChannelUsername, string ChannelTitle,
    int MessageId, string Text, DateTimeOffset PublishedAt, DateTimeOffset ReceivedAt);
