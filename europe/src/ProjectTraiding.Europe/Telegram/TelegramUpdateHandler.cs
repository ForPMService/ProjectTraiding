using TL;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Преобразует новые публикации заданных каналов в прикладной формат.</summary>
internal static class TelegramUpdateHandler
{
    /// <summary>Принимает только новые сообщения из настроенных каналов без фильтрации текста.</summary>
    internal static TelegramChannelPost? Convert(Update update, Dictionary<long, Channel> channels)
    {
        // Новая публикация канала наследуется от UpdateNewMessage; редактирование имеет другой тип обновления.
        if (update is not UpdateNewMessage { message: Message message }
            || message.peer_id is not PeerChannel peer
            || !channels.TryGetValue(peer.channel_id, out Channel? channel))
            return null;

        // Библиотека декодирует время TL из Unix-секунд в UTC; проверка готовности и текста не применяется.
        DateTimeOffset publishedAtUtc = new(message.date.Ticks, TimeSpan.Zero);
        return new(channel.id, channel.username, channel.title, message.id, message.message,
            publishedAtUtc, DateTimeOffset.UtcNow);
    }
}
