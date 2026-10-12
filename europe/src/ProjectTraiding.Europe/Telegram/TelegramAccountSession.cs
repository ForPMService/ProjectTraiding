using TL;
using WTelegram;

namespace ProjectTraiding.Europe.Telegram;

// Owns one client and its update manager for the entire host lifetime.
internal sealed class TelegramAccountSession(TelegramOptions options,
    Action<TelegramChannelPost> publish, Action<Exception> fail) : IAsyncDisposable
{
    private Client? _client;
    private UpdateManager? _manager;
    private Dictionary<long, Channel> _channels = [];
    private InputPeerUser? _destination;
    private Task? _request;

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(options.SessionPath) || new FileInfo(options.SessionPath).Length == 0)
                throw new InvalidOperationException("A previously authorized Telegram session is required.");

            Helpers.Log = DiscardLibraryLog;
            Directory.CreateDirectory(Path.GetDirectoryName(options.UpdateStatePath)!);
            _client = new Client(options.ApiId, options.ApiHash, options.SessionPath)
            {
                MaxCodePwdAttempts = 1,
                MaxAutoReconnects = 0,
                FloodRetryThreshold = 0
            };
            _client.OnOther += HandleOtherAsync;
            string? requested = await AwaitRequestAsync(_client.Login(options.PhoneNumber), cancellationToken);
            if (requested is not null)
                throw new InvalidOperationException("The Telegram session requires interactive authorization.");

            Messages_Dialogs dialogs = await AwaitRequestAsync(_client.Messages_GetAllDialogs(), cancellationToken);
            Dictionary<long, Channel> channels = [];
            foreach (string username in options.Channels)
            {
                string normalized = TelegramOptions.NormalizeUsername(username);
                Channel? match = null;
                foreach (ChatBase chat in dialogs.chats.Values)
                {
                    if (chat is Channel channel && (channel.flags & Channel.Flags.broadcast) != 0
                        && (channel.flags & (Channel.Flags.left | Channel.Flags.min)) == 0
                        && channel.access_hash != 0
                    && string.Equals(channel.username, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        match = channel;
                        break;
                    }
                }
                if (match is null)
                    throw new InvalidOperationException("A configured Telegram channel is unavailable.");
                channels[match.id] = match;
            }

            Contacts_ResolvedPeer resolved = await AwaitRequestAsync(
                _client.Contacts_ResolveUsername(TelegramOptions.NormalizeUsername(options.ForwardBotUsername)), cancellationToken);
            if (resolved.peer is not PeerUser peer
                || !resolved.users.TryGetValue(peer.user_id, out User? bot)
                || (bot.flags & User.Flags.bot) == 0 || (bot.flags & User.Flags.min) != 0
                || bot.access_hash == 0)
                throw new InvalidOperationException("The forwarding destination must be an accessible Telegram bot.");

            _channels = channels;
            _destination = new InputPeerUser(bot.id, bot.access_hash);
            // Register reception only after the fixed source map and destination are ready.
            _manager = _client.WithUpdateManager(HandleUpdateAsync, options.UpdateStatePath);
            _manager.Log = null;
            dialogs.CollectUsersChats(_manager.Users, _manager.Chats);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Library exception messages may contain account data; the host receives only a safe exception.
            throw new InvalidOperationException("Unable to initialize the authorized Telegram session, source channels or destination bot.");
        }
    }

    internal async Task ForwardAsync(TelegramChannelPost post, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Channel source = _channels[post.ChannelId];
            Message[] forwarded = await AwaitRequestAsync(_client!.ForwardMessagesAsync(
                new InputPeerChannel(source.id, source.access_hash), [post.MessageId],
                _destination!, drop_author: false), cancellationToken);
            if (forwarded.Length != 1 || forwarded[0] is not { id: > 0 })
                throw new InvalidOperationException("Telegram did not confirm the original forwarding.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new InvalidOperationException("Telegram original forwarding failed or was not confirmed.");
        }
    }

    private async Task<T> AwaitRequestAsync<T>(Task<T> request, CancellationToken cancellationToken)
    {
        // Initialization and forwarding are sequential: only one physical request can be outstanding.
        // WaitAsync cancels waiting, so a timed-out request is observed again after disposing the client.
        _request = request;
        try { return await request.WaitAsync(options.OperationTimeout, cancellationToken); }
        finally { if (request.IsCompleted) _request = null; }
    }

    private Task HandleUpdateAsync(Update update)
    {
        try
        {
            TelegramChannelPost? post = TelegramUpdateHandler.Convert(update, _channels);
            if (post is not null) publish(post);
        }
        catch (Exception)
        {
            fail(new InvalidOperationException("Telegram publication could not be delivered to the application."));
        }
        return Task.CompletedTask;
    }

    private Task HandleOtherAsync(IObject notification)
    {
        if (notification is ReactorError)
            fail(new IOException("Telegram connection failed."));
        return Task.CompletedTask;
    }

    private static void DiscardLibraryLog(int level, string message) { }

    public async ValueTask DisposeAsync()
    {
        if (_client is null) return;
        _client.OnOther -= HandleOtherAsync;
        try
        {
            await _client.DisposeAsync();
            try
            {
                if (_request is not null)
                {
                    try { await _request; }
                    // This is how WTelegram 4.4.9 aborts pending RPCs during DisposeAsync.
                    catch (ObjectDisposedException) { }
                    catch (OperationCanceledException) { }
                }
            }
            finally
            {
                // SaveState is synchronous in the library and must follow client disposal.
                _manager?.SaveState(options.UpdateStatePath);
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Telegram session could not be closed or its update state saved.");
        }
        finally
        {
            _client = null;
            _manager = null;
            _request = null;
        }
    }
}
