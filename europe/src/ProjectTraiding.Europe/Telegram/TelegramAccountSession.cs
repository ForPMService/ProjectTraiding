using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using TL;
using WTelegram;

namespace ProjectTraiding.Europe.Telegram;

// Commands own creation and closing; callback only signals the controller. No gate is acquired in callbacks.
internal sealed class TelegramAccountSession(TelegramOptions options, Action<Update> onUpdate,
    Action<TelegramErrorCategory> onFault, ILogger logger, IMeterFactory meterFactory)
{
    internal sealed record ChannelResolution(Dictionary<long, TelegramUpdateHandler.ChannelInfo> Channels,
        string? UnavailableUsername = null);
    private readonly object _sync = new();
    private readonly Task?[] _pending = new Task?[3];
    private readonly Histogram<double> _duration = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateHistogram<double>("telegram.mtproto.duration", "s");
    private readonly Counter<long> _outcomes = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateCounter<long>("telegram.mtproto.operations");
    private readonly ActivitySource _activities = new("ProjectTraiding.Europe.Telegram.Session");
    private Client? _client;
    private UpdateManager? _manager;
    private Task? _closing;
    private bool _unusable;

    internal bool Authorized { get; private set; }
    internal bool Closed { get; private set; }
    internal bool CleanupPending => _closing is { IsCompleted: false };

    internal async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(options.SessionPath) || new FileInfo(options.SessionPath).Length == 0) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(options.UpdateStatePath)!);
            Helpers.Log = DiscardLibraryLog;
            _client = new Client(options.ApiId, options.ApiHash, options.SessionPath)
            {
                MaxCodePwdAttempts = 1,
                MaxAutoReconnects = 0,
                FloodRetryThreshold = 0
            };
            _client.OnOther += HandleOtherAsync;
            _manager = _client.WithUpdateManager(HandleUpdateAsync, options.UpdateStatePath);
            _manager.Log = null;
            string? requested = await AwaitRpcAsync(_client.Login(options.PhoneNumber), "login", 0,
                options.OperationTimeout, cancellationToken);
            // There is no route for supplying verification codes or passwords in this application.
            Authorized = requested is null;
            return Authorized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TelegramSourceException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new TelegramBoundaryException(TelegramErrorCategory.StorageFailure, "session-open");
        }
        catch (Exception error) { throw TelegramSourceException.Classify(error, "session-open"); }
    }

    internal async Task<ChannelResolution> ResolveChannelsAsync(
        IReadOnlyList<string> usernames, CancellationToken cancellationToken)
    {
        Client client = GetClient();
        Messages_Dialogs dialogs = await AwaitRpcAsync(client.Messages_GetAllDialogs(), "dialogs", 1,
            options.OperationTimeout, cancellationToken);
        Dictionary<long, TelegramUpdateHandler.ChannelInfo> result = [];
        foreach (string username in usernames)
        {
            Channel? match = null;
            foreach (ChatBase chat in dialogs.chats.Values)
            {
                if (chat is Channel channel && (channel.flags & Channel.Flags.broadcast) != 0
                    && (channel.flags & (Channel.Flags.left | Channel.Flags.min)) == 0 && channel.access_hash != 0
                    && string.Equals(channel.username, username, StringComparison.OrdinalIgnoreCase))
                {
                    match = channel;
                    break;
                }
            }
            if (match is null)
                return new([], username);
            result.Add(match.id, new(username, match.title));
        }
        return new(result);
    }

    private Client GetClient()
    {
        lock (_sync)
        {
            if (_unusable || !Authorized || _client is null)
                throw new TelegramSourceException(TelegramErrorCategory.AuthorizationRequired, "connection");
            return _client;
        }
    }

    private Task HandleUpdateAsync(Update update)
    {
        try { onUpdate(update); }
        catch (Exception) { onFault(TelegramErrorCategory.Unexpected); }
        return Task.CompletedTask;
    }

    private Task HandleOtherAsync(IObject notification)
    {
        if (notification is ReactorError) onFault(TelegramErrorCategory.TransportUnavailable);
        return Task.CompletedTask;
    }

    private static void DiscardLibraryLog(int level, string message) { }

    private async Task<T> AwaitRpcAsync<T>(Task<T> request, string operation, int slot,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_sync) _pending[slot] = request;
        long started = Stopwatch.GetTimestamp();
        using Activity? activity = _activities.StartActivity(operation);
        string outcome = "success";
        try
        {
            return await request.WaitAsync(timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            lock (_sync) _unusable = true;
            onFault(TelegramErrorCategory.TransportUnavailable);
            throw;
        }
        catch (Exception error)
        {
            TelegramSourceException failure = TelegramSourceException.Classify(error, operation);
            outcome = failure.Category.ToString();
            if (error is TimeoutException)
            {
                lock (_sync) _unusable = true;
                // WaitAsync only cancels waiting; the owner closes this client before another RPC/client.
                onFault(TelegramErrorCategory.NetworkTimeout);
            }
            throw failure;
        }
        finally
        {
            _duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new("operation", operation), new("outcome", outcome));
            _outcomes.Add(1, new("operation", operation), new("outcome", outcome));
            if (request.IsCompleted) lock (_sync) _pending[slot] = null;
        }
    }

    internal async Task CloseAsync(CancellationToken cancellationToken)
    {
        Task closing;
        lock (_sync) closing = _closing ??= CloseClientAsync();
        try { await closing.WaitAsync(options.CloseTimeout, cancellationToken); }
        catch (TimeoutException)
        {
            throw new TelegramSourceException(TelegramErrorCategory.ShutdownTimeout, "session-close");
        }
    }

    private async Task CloseClientAsync()
    {
        try
        {
        Authorized = false;
        Client? client = _client;
        _client = null;
        if (client is not null)
        {
            client.OnOther -= HandleOtherAsync;
            try { await client.DisposeAsync(); }
            catch (Exception error) { throw TelegramSourceException.Classify(error, "session-close"); }
        }
        foreach (Task? pending in _pending)
        {
            if (pending is null) continue;
            try { await pending; }
            catch (Exception error)
            {
                TelegramSourceException failure = TelegramSourceException.Classify(error, "pending-rpc");
                TelegramLog.Failure(logger, failure.Operation, failure.Category);
            }
        }
        Closed = true;
        // Library SaveState is synchronous; its required order is DisposeAsync -> SaveState.
        try { _manager?.SaveState(options.UpdateStatePath); }
        catch (Exception)
        {
            throw new TelegramBoundaryException(TelegramErrorCategory.StorageFailure, "update-state-save");
        }
        }
        finally
        {
            _manager = null;
            _activities.Dispose();
        }
    }
}
