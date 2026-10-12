namespace ProjectTraiding.Europe.Telegram;

public enum TelegramReceiverState { Disabled, Stopped, Starting, AuthorizationRequired, Receiving, Stopping, Error }
public enum TelegramCommandOutcome { Applied, AlreadyApplied, InvalidInput, Unavailable, Conflict, Failed }
public enum TelegramErrorCategory
{
    InvalidConfiguration, AuthorizationRequired, SourceChannelUnavailable, ControlPeerUnavailable,
    AccessDeniedOrProtected, RateLimited, NetworkTimeout, TransportUnavailable, StorageFailure,
    QueueOverflow, ShutdownTimeout, Unexpected
}

/// <summary>Safe command outcome; never contains raw source exceptions or credentials.</summary>
public sealed record TelegramReceiverCommandResult(TelegramCommandOutcome Outcome,
    TelegramErrorCategory? ErrorCode = null, string? Message = null);

/// <summary>Aggregate state; accepted posts have no crash durability in prompt 1.</summary>
public sealed record TelegramReceiverStatus(TelegramReceiverState State, TelegramErrorCategory? LastErrorCode,
    string? LastError, DateTimeOffset? LastReceivedAtUtc, int QueueCount, int QueueCapacity,
    long AcceptedCount, long OverflowCount, long ForwardedCount, long ForwardFailureCount,
    long PossiblyLostCount, bool DrainCompleted, bool CleanupPending, string BotState);

/// <summary>Configured public channel and its availability in the current session.</summary>
public sealed record TelegramChannelStatus(string Username, long? ChannelId, bool Available);
