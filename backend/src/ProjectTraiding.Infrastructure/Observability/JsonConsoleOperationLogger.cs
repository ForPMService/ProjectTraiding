using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Observability;

public sealed class JsonConsoleOperationLogger : IOperationLogger
{
    private readonly ILogger<JsonConsoleOperationLogger> _logger;
    private readonly JsonSerializerOptions _options;

    public JsonConsoleOperationLogger(ILogger<JsonConsoleOperationLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public ValueTask LogAsync(OperationEvent operationEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (operationEvent is null)
        {
            return ValueTask.CompletedTask;
        }

        var json = JsonSerializer.Serialize(operationEvent, _options);
        _logger.LogInformation("{Json}", json);

        return ValueTask.CompletedTask;
    }
}
