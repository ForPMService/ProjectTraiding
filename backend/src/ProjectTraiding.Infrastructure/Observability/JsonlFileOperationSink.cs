using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Observability;

public sealed class JsonlFileOperationSink
{
    private readonly ILogger<JsonlFileOperationSink> _logger;
    private readonly ISecretRedactor _secretRedactor;
    private readonly JsonlFileSinkOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonlFileOperationSink(IOptions<JsonlFileSinkOptions> options, ISecretRedactor secretRedactor, ILogger<JsonlFileOperationSink> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _secretRedactor = secretRedactor ?? throw new ArgumentNullException(nameof(secretRedactor));
        _options = options?.Value ?? new JsonlFileSinkOptions();
        _serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task WriteAsync(OperationEvent operationEvent, CancellationToken cancellationToken = default)
    {
        if (operationEvent is null)
            return;

        if (!_options.Enabled)
            return;

        var redacted = RedactOperationEvent(operationEvent);

        string json;
        try
        {
            json = JsonSerializer.Serialize(redacted, _serializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize operation event for JSONL sink");
            return;
        }

        var filePath = ResolveFilePath(_options.FilePath);

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Append a single-line JSON record
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append operation event to JSONL file '{FilePath}'", filePath);
        }
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
                var byKey = _secretRedactor.RedactByKey(kvp.Key, kvp.Value);
                var intermediate = byKey ?? kvp.Value;
                var final = _secretRedactor.Redact(intermediate);
                dict[kvp.Key] = final ?? intermediate ?? string.Empty;
            }

            details = dict;
        }

        return operationEvent with { Message = message, Details = details };
    }

    private string ResolveFilePath(string? path)
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var resolved = path ?? $"storage/logs/api.{date}.jsonl";
        resolved = resolved.Replace("{yyyyMMdd}", date).Replace("{date}", date);

        if (!Path.IsPathRooted(resolved))
        {
            resolved = Path.Combine(Directory.GetCurrentDirectory(), resolved);
        }

        return resolved;
    }
}
