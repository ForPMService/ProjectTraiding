using Microsoft.Extensions.Hosting;

namespace ProjectTraiding.Telegram;

public sealed class TelegramChannelReceiverService(TelegramAccountSession session) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await session.StartAsync(stoppingToken);
            if (session.GetStatus().State == TelegramAccountSession.AccountState.Disabled) return;
            if (!await session.WaitForAuthorizationAsync(stoppingToken)) return;
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
