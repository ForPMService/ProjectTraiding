using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Observability;

public sealed class CombinedOperationLogger : IOperationLogger
{
    private readonly IOperationLogger _consoleLogger;
    private readonly JsonlFileOperationSink _fileSink;
    private readonly ILogger<CombinedOperationLogger> _logger;

    public CombinedOperationLogger(JsonConsoleOperationLogger consoleLogger, JsonlFileOperationSink fileSink, ILogger<CombinedOperationLogger> logger)
    {
        _consoleLogger = consoleLogger ?? throw new ArgumentNullException(nameof(consoleLogger));
        _fileSink = fileSink ?? throw new ArgumentNullException(nameof(fileSink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask LogAsync(OperationEvent operationEvent, CancellationToken cancellationToken = default)
    {
        if (operationEvent is null)
            return;

        try
        {
            var vt = _consoleLogger.LogAsync(operationEvent, cancellationToken);
            if (!vt.IsCompletedSuccessfully)
                await vt;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Console operation logger failed");
        }

        try
        {
            await _fileSink.WriteAsync(operationEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JSONL sink failed while writing operation event");
        }
    }
}
