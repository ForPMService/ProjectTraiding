using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ProjectTraiding.Api.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string? correlationId = null;

        if (context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            correlationId = values.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Items[ItemKey] = correlationId!;

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
            {
                context.Response.Headers[HeaderName] = correlationId!;
            }
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
