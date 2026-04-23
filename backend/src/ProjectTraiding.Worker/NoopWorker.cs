using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Worker;

public class NoopWorker : BackgroundService
{
    private readonly IOperationLogger _operationLogger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly string _correlationId;

    public NoopWorker(IOperationLogger operationLogger, IHostEnvironment hostEnvironment)
    {
        _operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
        _correlationId = Guid.NewGuid().ToString();
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var starting = new OperationEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Level: "Information",
            ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
            Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
            OperationName: "worker starting",
            Message: "Worker starting",
            CorrelationId: _correlationId
        );

        await _operationLogger.LogAsync(starting, CancellationToken.None).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startedLoop = new OperationEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Level: "Information",
            ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
            Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
            OperationName: "worker started loop",
            Message: "Worker entered main loop",
            CorrelationId: _correlationId
        );

        await _operationLogger.LogAsync(startedLoop, CancellationToken.None).ConfigureAwait(false);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var heartbeat = new OperationEvent(
                    Timestamp: DateTimeOffset.UtcNow,
                    Level: "Information",
                    ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
                    Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
                    OperationName: "worker heartbeat",
                    Message: "Heartbeat",
                    CorrelationId: _correlationId
                );

                await _operationLogger.LogAsync(heartbeat, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var failed = new OperationEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Level: "Error",
                ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
                Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
                OperationName: "worker failure",
                Message: ex.Message,
                CorrelationId: _correlationId,
                ExceptionType: ex.GetType().FullName
            );

            await _operationLogger.LogAsync(failed, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopping = new OperationEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Level: "Information",
            ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
            Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
            OperationName: "worker stopping",
            Message: "Worker stopping",
            CorrelationId: _correlationId
        );

        await _operationLogger.LogAsync(stopping, CancellationToken.None).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        var stopped = new OperationEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Level: "Information",
            ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
            Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
            OperationName: "worker stopped",
            Message: "Worker stopped",
            CorrelationId: _correlationId
        );

        await _operationLogger.LogAsync(stopped, CancellationToken.None).ConfigureAwait(false);
    }
}
