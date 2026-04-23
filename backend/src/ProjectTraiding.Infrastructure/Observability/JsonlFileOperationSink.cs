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

public sealed class JsonlFileOperationSink : IDisposable
{
    private readonly ILogger<JsonlFileOperationSink> _logger;
    private readonly OperationEventRedactor _redactor;
    private readonly JsonlFileSinkOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly System.Threading.SemaphoreSlim _writeLock = new(1, 1);

    internal JsonlFileOperationSink(IOptions<JsonlFileSinkOptions> options, OperationEventRedactor redactor, ILogger<JsonlFileOperationSink> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
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

        var redacted = _redactor.Redact(operationEvent);

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

            // Append a single-line JSON record (synchronized within process)
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(filePath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append operation event to JSONL file '{FilePath}'", filePath);
        }
    }
    public void Dispose()
    {
        _writeLock.Dispose();
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
