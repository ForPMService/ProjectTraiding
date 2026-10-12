using Microsoft.Extensions.Hosting;

namespace ProjectTraiding.Europe.Telegram;

// The controller owns the consumer; this hosted role observes faults and orders host shutdown.
internal sealed class EuropeTelegramRuntime(TelegramReceiverController controller, TelegramOptions options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (options.Enabled && options.AutoStart) await controller.StartAsync(stoppingToken);
            await controller.WatchFaultsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await base.StopAsync(cancellationToken); }
        finally { await controller.StopAsync(CancellationToken.None); }
    }
}
