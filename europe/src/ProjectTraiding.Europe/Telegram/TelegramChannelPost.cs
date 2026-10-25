namespace ProjectTraiding.Europe.Telegram;

/// <summary>Неизменяемые данные принятой публикации; оба момента времени указаны в UTC.</summary>
public sealed record TelegramChannelPost(long ChannelId, string? ChannelUsername, string ChannelTitle,
    int MessageId, string Text, DateTimeOffset PublishedAt, DateTimeOffset ReceivedAt);

/// <summary>Технические поля Telegram, сохраняемые рядом с общей публикацией.</summary>
public sealed record NewsTelegramMetadataDTO(long ChannelId, string? ChannelUsername, string ChannelTitle,
    int MessageId);
