using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using ProjectTraiding.Telegram;

namespace ProjectTraiding.Api.Telegram;

public static class TelegramAuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapTelegramAuthorizationEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/telegram/auth/status", GetStatus);
        routes.MapPost("/telegram/auth/answer", (Func<HttpContext, Task<IResult>>)AnswerAsync);
        return routes;
    }

    private static IResult GetStatus(HttpContext context)
    {
        if (!IsLocal(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        return Status(context.RequestServices.GetRequiredService<TelegramAccountSession>(), StatusCodes.Status200OK);
    }

    private static async Task<IResult> AnswerAsync(HttpContext context)
    {
        if (!IsLocal(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        TelegramAccountSession session = context.RequestServices.GetRequiredService<TelegramAccountSession>();
        if (session.GetStatus().State == TelegramAccountSession.AccountState.Disabled)
            return Status(session, StatusCodes.Status409Conflict);
        if (!context.Request.HasJsonContentType()) return Status(session, StatusCodes.Status415UnsupportedMediaType);
        const long maxBodySize = 4096;
        if (context.Request.ContentLength > maxBodySize) return Status(session, StatusCodes.Status413PayloadTooLarge);
        IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = maxBodySize;
        TelegramAuthAnswerRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(
                TelegramAuthJsonContext.Default.TelegramAuthAnswerRequest, context.RequestAborted);
        }
        catch (JsonException) { return Status(session, StatusCodes.Status400BadRequest); }
        catch (BadHttpRequestException) { return Status(session, StatusCodes.Status400BadRequest); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Status(session, StatusCodes.Status400BadRequest);
        }
        if (request is null) return Status(session, StatusCodes.Status400BadRequest);
        TelegramAccountSession.AnswerResult result = await session.AnswerAsync(request.ExpectedField, request.Value);
        int code = result switch
        {
            TelegramAccountSession.AnswerResult.Accepted => StatusCodes.Status200OK,
            TelegramAccountSession.AnswerResult.InvalidInput => StatusCodes.Status400BadRequest,
            TelegramAccountSession.AnswerResult.Failed => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status409Conflict
        };
        return Status(session, code);
    }

    private static bool IsLocal(HttpContext context)
    {
        IPAddress? address = context.Connection.RemoteIpAddress;
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }

    private static IResult Status(TelegramAccountSession session, int statusCode)
    {
        TelegramAccountSession.AuthorizationStatus status = session.GetStatus();
        return Results.Json(new TelegramAuthStatusResponse(status.State.ToString(), status.ExpectedField),
            TelegramAuthJsonContext.Default.TelegramAuthStatusResponse, statusCode: statusCode);
    }
}
