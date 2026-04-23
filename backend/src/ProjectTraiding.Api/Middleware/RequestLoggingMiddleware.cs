using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Shared.Observability;
using ProjectTraiding.Api.Observability;

namespace ProjectTraiding.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOperationLogger _operationLogger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly IHostEnvironment _hostEnvironment;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        IOperationLogger operationLogger,
        ICorrelationIdAccessor correlationIdAccessor,
        IHostEnvironment hostEnvironment)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
        _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        var operationName = string.Concat(method, " ", path);
        var correlationId = _correlationIdAccessor.GetCorrelationId() ?? string.Empty;

        var started = new OperationEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Level: "Information",
            ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
            Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
            OperationName: operationName,
            Message: "Request started",
            CorrelationId: correlationId,
            EndpointGroup: path
        );

        // Log start without blocking request processing
        await _operationLogger.LogAsync(started, CancellationToken.None).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context).ConfigureAwait(false);

            sw.Stop();

            var finishedDetails = new Dictionary<string, string>
            {
                ["status_code"] = context.Response?.StatusCode.ToString() ?? "",
                ["duration_ms"] = sw.ElapsedMilliseconds.ToString()
            };

            var finished = new OperationEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Level: "Information",
                ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
                Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
                OperationName: operationName,
                Message: "Request finished",
                CorrelationId: correlationId,
                EndpointGroup: path,
                Details: finishedDetails
            );

            await _operationLogger.LogAsync(finished, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();

            var errorDetails = new Dictionary<string, string>
            {
                ["message"] = ex.Message,
                ["duration_ms"] = sw.ElapsedMilliseconds.ToString()
            };

            var failed = new OperationEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Level: "Error",
                ServiceName: _hostEnvironment.ApplicationName ?? string.Empty,
                Environment: _hostEnvironment.EnvironmentName ?? string.Empty,
                OperationName: operationName,
                Message: "Request failed",
                CorrelationId: correlationId,
                EndpointGroup: path,
                ExceptionType: ex.GetType().FullName,
                Details: errorDetails
            );

            // Log the failure and rethrow
            await _operationLogger.LogAsync(failed, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
