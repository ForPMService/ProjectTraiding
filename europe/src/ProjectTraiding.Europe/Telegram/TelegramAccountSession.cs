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
        Dictionary<long, InputPeer> SourcePeers, InputPeer? ControlPeer,
        string? UnavailableUsername = null);
    internal sealed record TelegramForwardResult(bool Forwarded, TelegramErrorCategory? ErrorCode = null);
    private readonly object _sync = new();
    private readonly Task?[] _pending = new Task?[3];
    private readonly Histogram<double> _duration = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateHistogram<double>("telegram.mtproto.duration", "s");
    private readonly Counter<long> _outcomes = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateCounter<long>("telegram.mtproto.operations");
    private readonly ActivitySource _activities = new("ProjectTraiding.Europe.Telegram.Session");
    private Client? _client;
    private UpdateManager? _manager;
    private Task? _closing;
    private bool _unusable;
    private Dictionary<long, InputPeer> _sourcePeers = [];
    private HashSet<long> _activeSources = [];
    private readonly Dictionary<long, int> _pendingBySource = [];
    private InputPeer? _controlPeer;

    internal bool Authorized { get; private set; }
    internal bool Closed { get; private set; }

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
        dialogs.CollectUsersChats(_manager!.Users, _manager.Chats);
        Dictionary<long, TelegramUpdateHandler.ChannelInfo> result = [];
        Dictionary<long, InputPeer> peers = [];
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
                return new([], [], null, username);
            // Forwarding into a subscribed broadcast destination would create a feedback loop.
            if (options.ControlPeerKind == "Channel" && match.id == options.ControlPeerId)
                return new([], [], null, username);
            result.Add(match.id, new(username, match.title));
            peers.Add(match.id, new InputPeerChannel(match.id, match.access_hash));
        }
        return new(result, peers, ResolveControlPeer(dialogs));
    }

    internal void ApplyChannels(ChannelResolution resolution)
    {
        lock (_sync)
        {
            HashSet<long> active = new(resolution.SourcePeers.Keys);
            foreach (long channelId in _pendingBySource.Keys)
                if (!resolution.SourcePeers.ContainsKey(channelId) && _sourcePeers.TryGetValue(channelId, out InputPeer? peer))
                    resolution.SourcePeers.Add(channelId, peer);
            _sourcePeers = resolution.SourcePeers;
            _activeSources = active;
            _controlPeer = resolution.ControlPeer;
        }
    }

    internal void RetainSource(long channelId)
    {
        lock (_sync)
        {
            _pendingBySource.TryGetValue(channelId, out int count);
            _pendingBySource[channelId] = count + 1;
        }
    }

    internal void ReleaseSource(long channelId)
    {
        lock (_sync)
        {
            if (!_pendingBySource.TryGetValue(channelId, out int count)) return;
            if (count > 1) { _pendingBySource[channelId] = count - 1; return; }
            _pendingBySource.Remove(channelId);
            if (!_activeSources.Contains(channelId)) _sourcePeers.Remove(channelId);
        }
    }

    internal void RetireSource(long channelId)
    {
        lock (_sync)
        {
            _activeSources.Remove(channelId);
            if (!_pendingBySource.ContainsKey(channelId)) _sourcePeers.Remove(channelId);
        }
    }

    private InputPeer? ResolveControlPeer(Messages_Dialogs dialogs)
    {
        if (options.ControlPeerKind == "User")
        {
            if (dialogs.users.TryGetValue(options.ControlPeerId, out User? user)
                && (user.flags & User.Flags.min) == 0 && user.access_hash != 0)
                return new InputPeerUser(user.id, user.access_hash);
            return null;
        }
        if (!dialogs.chats.TryGetValue(options.ControlPeerId, out ChatBase? chat)) return null;
        if (options.ControlPeerKind == "Chat" && chat is Chat group && (group.flags & Chat.Flags.left) == 0)
            return new InputPeerChat(group.id);
        if (options.ControlPeerKind == "Channel" && chat is Channel channel
            && (channel.flags & (Channel.Flags.left | Channel.Flags.min)) == 0 && channel.access_hash != 0)
            return new InputPeerChannel(channel.id, channel.access_hash);
        return null;
    }

    internal async Task<TelegramForwardResult> ForwardAsync(TelegramChannelPost post, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InputPeer? source;
        InputPeer? destination;
        lock (_sync)
        {
            _sourcePeers.TryGetValue(post.ChannelId, out source);
            destination = _controlPeer;
        }
        if (source is null) return new(false, TelegramErrorCategory.SourceChannelUnavailable);
        if (destination is null) return new(false, TelegramErrorCategory.ControlPeerUnavailable);
        Client client = GetClient();
        Message[] forwarded = await AwaitRpcAsync(client.ForwardMessagesAsync(source, [post.MessageId],
            destination, drop_author: false), "forward", 2, options.ForwardTimeout, cancellationToken);
        // A completed request without a confirmed forwarded message is not reported as delivery.
        return forwarded.Length == 1 && forwarded[0] is { id: > 0 }
            ? new(true) : new(false, TelegramErrorCategory.Unexpected);
    }

    private Client GetClient()
    {
        lock (_sync)
        {
            if (_unusable)
                throw new TelegramSourceException(TelegramErrorCategory.TransportUnavailable, "connection");
            if (!Authorized || _client is null)
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
        // Closing is invoked only by the serialized command owner. Do not hold _sync while Dispose starts:
        // library completion callbacks can need the controller's admission lock (controller -> session order).
        closing = _closing ??= CloseClientAsync();
        try { await closing.WaitAsync(options.CloseTimeout, cancellationToken); }
        catch (TimeoutException)
        {
            throw new TelegramSourceException(TelegramErrorCategory.ShutdownTimeout, "session-close");
        }
    }

    private async Task CloseClientAsync()
    {
        // Publish the tracked closing task before synchronous library state persistence can run.
        await Task.Yield();
        try
        {
            Client? client;
            lock (_sync)
            {
                Authorized = false;
                client = _client;
                _client = null;
            }
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
                catch (OperationCanceledException)
                {
                    // Client disposal cancels outstanding library RPCs; this is normal shutdown.
                }
                catch (Exception error)
                {
                    TelegramSourceException failure = TelegramSourceException.Classify(error, "pending-rpc");
                    TelegramLog.Failure(logger, failure.Operation, failure.Category);
                }
            }
            // Library SaveState is synchronous; its required order is DisposeAsync -> SaveState.
            long started = Stopwatch.GetTimestamp();
            string outcome = "success";
            try { _manager?.SaveState(options.UpdateStatePath); }
            catch (Exception)
            {
                outcome = "StorageFailure";
                throw new TelegramBoundaryException(TelegramErrorCategory.StorageFailure, "update-state-save");
            }
            finally
            {
                _duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                    new("operation", "update-state-save"), new("outcome", outcome));
                _outcomes.Add(1, new("operation", "update-state-save"), new("outcome", outcome));
                Closed = true;
            }
        }
        finally
        {
            _manager = null;
            _activities.Dispose();
        }
    }
}
