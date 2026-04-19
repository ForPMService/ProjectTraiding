using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class NoopWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Placeholder worker - does nothing, kept minimal intentionally.
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}
