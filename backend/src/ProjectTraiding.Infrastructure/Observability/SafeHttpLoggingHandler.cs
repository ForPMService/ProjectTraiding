using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Observability;

/// <summary>
/// DelegatingHandler that emits minimal, safe logs about outgoing HTTP requests.
/// It avoids logging sensitive headers and bodies and uses <see cref="ISecretRedactor"/>
/// to mask potentially sensitive query values.
/// </summary>
public sealed class SafeHttpLoggingHandler : DelegatingHandler
{
    private readonly IOperationLogger _operationLogger;
    private readonly ISecretRedactor _secretRedactor;

    public const string CorrelationHeader = "X-Correlation-Id";

    public SafeHttpLoggingHandler(IOperationLogger operationLogger, ISecretRedactor secretRedactor)
    {
        _operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        _secretRedactor = secretRedactor ?? throw new ArgumentNullException(nameof(secretRedactor));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        var method = request.Method?.Method ?? string.Empty;
        var uri = request.RequestUri;
        var sanitizedUri = SanitizeUri(uri);
        var host = uri?.Host ?? string.Empty;
        var path = uri?.AbsolutePath ?? string.Empty;
        var correlationId = ExtractCorrelationId(request) ?? string.Empty;

        var operationName = string.Concat(method, " ", path);

        var startedDetails = new Dictionary<string, string>
        {
            ["method"] = method,
            ["uri"] = sanitizedUri,
            ["host"] = host,
            ["path"] = path
        };

        var started = new OperationEvent(
            Timestamp: startedAt,
            Level: "Information",
            ServiceName: string.Empty,
            Environment: string.Empty,
            OperationName: operationName,
            Message: "Outgoing request started",
            CorrelationId: correlationId,
            EndpointGroup: path,
            Details: startedDetails
        );

        // Fire-and-forget style start log (logger implementations can choose to be async)
        await _operationLogger.LogAsync(started, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            var finishedDetails = new Dictionary<string, string>
            {
                ["method"] = method,
                ["uri"] = sanitizedUri,
                ["host"] = host,
                ["path"] = path,
                ["status_code"] = ((int)response.StatusCode).ToString(),
                ["duration_ms"] = sw.ElapsedMilliseconds.ToString()
            };

            var finished = new OperationEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Level: "Information",
                ServiceName: string.Empty,
                Environment: string.Empty,
                OperationName: operationName,
                Message: "Outgoing request finished",
                CorrelationId: correlationId,
                EndpointGroup: path,
                Details: finishedDetails
            );

            await _operationLogger.LogAsync(finished, cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();

            var errorDetails = new Dictionary<string, string>
            {
                ["method"] = method,
                ["uri"] = sanitizedUri,
                ["host"] = host,
                ["path"] = path,
                ["duration_ms"] = sw.ElapsedMilliseconds.ToString(),
                ["message"] = ex.Message
            };

            var failed = new OperationEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Level: "Error",
                ServiceName: string.Empty,
                Environment: string.Empty,
                OperationName: operationName,
                Message: "Outgoing request failed",
                CorrelationId: correlationId,
                EndpointGroup: path,
                ExceptionType: ex.GetType().FullName,
                Details: errorDetails
            );

            await _operationLogger.LogAsync(failed, cancellationToken).ConfigureAwait(false);

            throw;
        }
    }

    private string? ExtractCorrelationId(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues(CorrelationHeader, out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    private string SanitizeUri(Uri? uri)
    {
        if (uri is null) return string.Empty;

        try
        {
            var builder = new UriBuilder(uri);

            // Parse and redact query parameters conservatively
            var rawQuery = builder.Query ?? string.Empty; // includes leading '?'
            var query = rawQuery.Length > 0 ? rawQuery.Substring(1) : string.Empty;

            if (string.IsNullOrEmpty(query))
            {
                // Return scheme://host[:port]/path
                return BuildWithoutUserInfo(builder);
            }

            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>(parts.Length);

            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                var key = Uri.UnescapeDataString(kv[0] ?? string.Empty);
                var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;

                // First try redaction by key (explicit sensitive query params), then redact by heuristics
                var byKey = _secretRedactor.RedactByKey(key, value);
                var intermediate = byKey ?? value;
                var final = _secretRedactor.Redact(intermediate) ?? intermediate ?? string.Empty;

                var encodedKey = Uri.EscapeDataString(key);
                var encodedValue = Uri.EscapeDataString(final);
                kept.Add(string.Concat(encodedKey, "=", encodedValue));
            }

            builder.Query = string.Join("&", kept);

            // Ensure we never produce a double question mark. UriBuilder.Query getter
            // may include a leading '?'. Normalize by removing it before concatenation.
            var q = builder.Query ?? string.Empty;
            if (q.Length > 0 && q.StartsWith("?", StringComparison.Ordinal))
            {
                q = q.Substring(1);
            }

            return BuildWithoutUserInfo(builder) + (q.Length > 0 ? "?" + q : string.Empty);
        }
        catch
        {
            // On any parsing failure, return a conservative representation
            return uri.GetLeftPart(UriPartial.Path);
        }
    }

    private static string BuildWithoutUserInfo(UriBuilder builder)
    {
        var sb = new StringBuilder();
        sb.Append(builder.Scheme);
        sb.Append("://");
        sb.Append(builder.Host);
        if (!builder.Port.Equals(80) && !builder.Port.Equals(443))
        {
            sb.Append(":" + builder.Port);
        }
        sb.Append(builder.Path);
        return sb.ToString();
    }
}
