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
    private readonly ISecretRedactor _secretRedactor;

    public JsonConsoleOperationLogger(ILogger<JsonConsoleOperationLogger> logger, ISecretRedactor secretRedactor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _secretRedactor = secretRedactor ?? throw new ArgumentNullException(nameof(secretRedactor));
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

        var redacted = RedactOperationEvent(operationEvent);

        var json = JsonSerializer.Serialize(redacted, _options);
        _logger.LogInformation("{Json}", json);

        return ValueTask.CompletedTask;
    }

    private OperationEvent RedactOperationEvent(OperationEvent operationEvent)
    {
        var message = _secretRedactor.Redact(operationEvent.Message) ?? string.Empty;

        IReadOnlyDictionary<string, string>? details = null;
        if (operationEvent.Details != null)
        {
            var dict = new Dictionary<string, string>(operationEvent.Details.Count);
            foreach (var kvp in operationEvent.Details)
            {
                var red = _secretRedactor.RedactByKey(kvp.Key, kvp.Value);
                dict[kvp.Key] = red ?? kvp.Value ?? string.Empty;
            }

            details = dict;
        }

        return operationEvent with { Message = message, Details = details };
    }
}
