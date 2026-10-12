using System.Net.Sockets;
using TL;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Sanitized external failure. Raw exceptions are deliberately not retained as inner exceptions.</summary>
public class TelegramBoundaryException(TelegramErrorCategory category, string operation,
    bool isRetryable = false, TimeSpan? retryAfter = null, int? sourceCode = null)
    : Exception($"{operation}: {category}")
{
    public TelegramErrorCategory Category { get; } = category;
    public string Operation { get; } = operation;
    public bool IsRetryable { get; } = isRetryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public int? SourceCode { get; } = sourceCode;
}

/// <summary>MTProto error policy, isolated from commands and filesystem failures.</summary>
internal sealed class TelegramSourceException(TelegramErrorCategory category, string operation,
    bool isRetryable = false, TimeSpan? retryAfter = null, int? sourceCode = null)
    : TelegramBoundaryException(category, operation, isRetryable, retryAfter, sourceCode)
{
    internal static TelegramSourceException Classify(Exception error, string operation)
    {
        if (error is RpcException rpc)
        {
            if (rpc.Code == 420 || rpc.Message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
                return new(TelegramErrorCategory.RateLimited, operation, true,
                    rpc.X > 0 ? TimeSpan.FromSeconds(rpc.X) : null, rpc.Code);
            if (rpc.Code == 401)
                return new(TelegramErrorCategory.AuthorizationRequired, operation, false, null, rpc.Code);
            if (rpc.Code == 403 || rpc.Message.Contains("FORWARDS_RESTRICTED", StringComparison.Ordinal))
                return new(TelegramErrorCategory.AccessDeniedOrProtected, operation, false, null, rpc.Code);
            if (rpc.Code == 400 && operation == "dialogs")
                return new(TelegramErrorCategory.SourceChannelUnavailable, operation, false, null, rpc.Code);
            return new(TelegramErrorCategory.TransportUnavailable, operation, rpc.Code >= 500, null, rpc.Code);
        }
        return error switch
        {
            TimeoutException => new(TelegramErrorCategory.NetworkTimeout, operation, true),
            IOException or SocketException => new(TelegramErrorCategory.TransportUnavailable, operation, true),
            _ => new(TelegramErrorCategory.Unexpected, operation)
        };
    }
}
