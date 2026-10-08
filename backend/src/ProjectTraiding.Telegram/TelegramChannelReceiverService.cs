using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TL;

namespace ProjectTraiding.Telegram;

public sealed class TelegramChannelReceiverService(TelegramAccountSession session,
    ILogger<TelegramChannelReceiverService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await session.StartAsync(stoppingToken);
            if (session.GetStatus().State == TelegramAccountSession.AccountState.Disabled) return;
            if (!await session.WaitForAuthorizationAsync(stoppingToken)) return;
            var connection = session.GetAuthorizedConnection();
            if (connection is null) return;
            var dialogs = await connection.Value.Client.Messages_GetAllDialogs().WaitAsync(stoppingToken);
            dialogs.CollectUsersChats(connection.Value.Manager.Users, connection.Value.Manager.Chats);

            Dictionary<long, TelegramUpdateHandler.ChannelInfo> channels = [];
            bool missingChannel = false;
            foreach (string configured in session.ConfiguredChannels)
            {
                string username = configured.TrimStart('@');
                Channel? match = null;
                // Используем снимок ответа dialogs: словари manager параллельно пополняются updates.
                foreach (ChatBase chat in dialogs.chats.Values)
                {
                    if (chat is Channel channel && (channel.flags & Channel.Flags.broadcast) != 0
                        && (channel.flags & Channel.Flags.left) == 0
                        && string.Equals(channel.username, username, StringComparison.OrdinalIgnoreCase))
                    {
                        match = channel;
                        break;
                    }
                }
                if (match is null)
                {
                    missingChannel = true;
                    logger.LogError("Telegram: доступный broadcast-канал {ChannelUsername} не найден.", username);
                    continue;
                }
                channels[match.id] = new(match.username, match.title, true);
            }
            if (missingChannel)
            {
                await session.FailAsync("не все настроенные каналы сопоставлены; приём не включён");
                return;
            }
            session.BeginReceiving(channels);
            await session.WaitUntilFinishedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception) { await session.FailAsync("ошибка фоновой службы Telegram"); }
        finally { await session.StopAsync(); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Освобождение соединения прерывает сетевой Login до ожидания завершения службы.
        await session.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
