using System;
using System.Collections.Generic;
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
    private readonly OperationEventRedactor _redactor;

    internal JsonConsoleOperationLogger(ILogger<JsonConsoleOperationLogger> logger, OperationEventRedactor redactor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
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

        var redacted = _redactor.Redact(operationEvent);

        var json = JsonSerializer.Serialize(redacted, _options);
        _logger.LogInformation("{Json}", json);

        return ValueTask.CompletedTask;
    }

    
}
