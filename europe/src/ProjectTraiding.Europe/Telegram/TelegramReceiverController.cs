using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TL;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Single owner of receiver commands, admission and queue generations.</summary>
public sealed class TelegramReceiverController : IDisposable
{
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramReceiverController> _logger;
    private readonly IMeterFactory _meterFactory;
    private readonly TelegramChannelCatalog _catalog;
    private bool _catalogLoaded;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly SemaphoreSlim _faultSignal = new(0, 1);
    private readonly Counter<long> _received;
    private readonly Counter<long> _admission;
    private readonly Counter<long> _transitions;
    private TelegramReceiverState _state;
    private TelegramErrorCategory? _lastErrorCode;
    private string? _lastError;
    private DateTimeOffset? _lastReceivedAtUtc;
    private DateTimeOffset _readyAtUtc;
    private string[] _usernames = [];
    private Dictionary<long, TelegramUpdateHandler.ChannelInfo> _snapshot = [];
    private TelegramAccountSession? _session;
    private Channel<TelegramChannelPost>? _queue;
    private CancellationTokenSource? _consumerCancellation;
    private Task? _consumer;
    private bool _accepting;
    private bool _drainCompleted = true;
    private bool _drainAborted;
    private long _accepted;
    private long _overflow;
    private long _forwarded = 0;
    private long _forwardFailures = 0;
    private long _possiblyLost;
    private string _botState = "Disabled";

    public TelegramReceiverController(TelegramOptions options, ILogger<TelegramReceiverController> logger,
        IMeterFactory meterFactory)
    {
        _options = options;
        _logger = logger;
        _meterFactory = meterFactory;
        _catalog = new(options, logger, meterFactory);
        _state = options.Enabled ? TelegramReceiverState.Stopped : TelegramReceiverState.Disabled;
        Meter meter = meterFactory.Create("ProjectTraiding.Europe.Telegram");
        _received = meter.CreateCounter<long>("telegram.updates.received");
        _admission = meter.CreateCounter<long>("telegram.publications.admission");
        _transitions = meter.CreateCounter<long>("telegram.receiver.transitions");
    }

    /// <summary>Returns a snapshot without waiting for network operations.</summary>
    public TelegramReceiverStatus GetStatus()
    {
        lock (_stateLock)
            return new(_state, _lastErrorCode, _lastError, _lastReceivedAtUtc,
                _queue?.Reader.Count ?? 0, _options.QueueCapacity, _accepted, _overflow,
                _forwarded, _forwardFailures, _possiblyLost, _drainCompleted,
                _session is not null && _state is TelegramReceiverState.Error or TelegramReceiverState.Stopping,
                _botState);
    }

    /// <summary>Returns a detached immutable view of channel availability.</summary>
    public IReadOnlyList<TelegramChannelStatus> GetChannels()
    {
        lock (_stateLock)
        {
            TelegramChannelStatus[] result = new TelegramChannelStatus[_usernames.Length];
            for (int index = 0; index < _usernames.Length; index++)
            {
                long? channelId = null;
                foreach (KeyValuePair<long, TelegramUpdateHandler.ChannelInfo> entry in _snapshot)
                    if (entry.Value.Username == _usernames[index]) { channelId = entry.Key; break; }
                result[index] = new(_usernames[index], channelId, _accepting && channelId.HasValue);
            }
            return Array.AsReadOnly(result);
        }
    }

    /// <summary>Starts one queue generation; never initiates a first authorization.</summary>
    public async Task<TelegramReceiverCommandResult> StartAsync(CancellationToken cancellationToken)
    {
        if (!await _commands.WaitAsync(_options.CommandWaitTimeout, cancellationToken)) return new(TelegramCommandOutcome.Conflict);
        try
        {
            lock (_stateLock)
            {
                if (_state == TelegramReceiverState.Disabled) return new(TelegramCommandOutcome.Unavailable);
                if (_state == TelegramReceiverState.Receiving) return new(TelegramCommandOutcome.AlreadyApplied);
                if (_session is not null || _consumer is not null) return new(TelegramCommandOutcome.Conflict);
                _lastErrorCode = null;
                _lastError = null;
                SetStateLocked(TelegramReceiverState.Starting);
            }
            try { await EnsureCatalogAsync(cancellationToken); }
            catch (TelegramStorageException failure)
            {
                RecordFailure(failure);
                return new(TelegramCommandOutcome.Failed, failure.Category);
            }
            TelegramAccountSession session = new(_options, HandleUpdate, SignalFault, _logger, _meterFactory);
            lock (_stateLock) _session = session;
            try
            {
                if (!await session.StartAsync(cancellationToken))
                {
                    await CleanupAsync(CancellationToken.None);
                    lock (_stateLock)
                    {
                        if (_session is null)
                        {
                            SetErrorLocked(TelegramErrorCategory.AuthorizationRequired);
                            SetStateLocked(TelegramReceiverState.AuthorizationRequired);
                        }
                    }
                    return new(TelegramCommandOutcome.Unavailable, TelegramErrorCategory.AuthorizationRequired);
                }
                TelegramAccountSession.ChannelResolution resolution = await session.ResolveChannelsAsync(_usernames, cancellationToken);
                if (resolution.UnavailableUsername is not null)
                {
                    lock (_stateLock)
                    {
                        SetErrorLocked(TelegramErrorCategory.SourceChannelUnavailable);
                        _lastError = "SourceChannelUnavailable: @" + resolution.UnavailableUsername;
                        SetStateLocked(TelegramReceiverState.Error);
                    }
                    await CleanupAsync(CancellationToken.None);
                    return new(TelegramCommandOutcome.Unavailable, TelegramErrorCategory.SourceChannelUnavailable);
                }
                lock (_stateLock)
                {
                    if (_state == TelegramReceiverState.Error)
                        return new(TelegramCommandOutcome.Failed, _lastErrorCode);
                    _queue = System.Threading.Channels.Channel.CreateBounded<TelegramChannelPost>(new BoundedChannelOptions(_options.QueueCapacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        AllowSynchronousContinuations = false,
                        SingleReader = true,
                        SingleWriter = false
                    });
                    _snapshot = resolution.Channels;
                    _consumerCancellation = new CancellationTokenSource();
                    _consumer = ConsumeAsync(_queue.Reader, _consumerCancellation.Token);
                    _readyAtUtc = DateTimeOffset.UtcNow;
                    _accepting = true;
                    _drainCompleted = false;
                    _drainAborted = false;
                    SetStateLocked(TelegramReceiverState.Receiving);
                }
                return new(TelegramCommandOutcome.Applied);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CleanupAsync(CancellationToken.None);
                lock (_stateLock) if (_session is null) SetStateLocked(TelegramReceiverState.Stopped);
                throw;
            }
            catch (TelegramBoundaryException failure)
            {
                RecordFailure(failure);
                await CleanupAsync(CancellationToken.None);
                return new(TelegramCommandOutcome.Failed, failure.Category);
            }
        }
        finally { _commands.Release(); }
    }

    /// <summary>Closes admission, drains the bounded queue and then closes the session.</summary>
    public async Task<TelegramReceiverCommandResult> StopAsync(CancellationToken cancellationToken)
    {
        if (!await _commands.WaitAsync(_options.CommandWaitTimeout, cancellationToken)) return new(TelegramCommandOutcome.Conflict);
        try
        {
            lock (_stateLock)
            {
                if (_state == TelegramReceiverState.Disabled || _state == TelegramReceiverState.Stopped && _session is null)
                    return new(TelegramCommandOutcome.AlreadyApplied);
                _accepting = false;
                _queue?.Writer.TryComplete();
                SetStateLocked(TelegramReceiverState.Stopping);
            }
            // Once admitted, stop is completed by its owner even if the command caller disconnects.
            bool cleaned = await CleanupAsync(CancellationToken.None);
            lock (_stateLock)
            {
                if (!cleaned || _session is not null || _consumer is not null || !_drainCompleted)
                {
                    SetStateLocked(TelegramReceiverState.Error);
                    return new(TelegramCommandOutcome.Failed, _lastErrorCode);
                }
                SetStateLocked(TelegramReceiverState.Stopped);
            }
            return new(TelegramCommandOutcome.Applied);
        }
        finally { _commands.Release(); }
    }

    private async Task<bool> CleanupAsync(CancellationToken cancellationToken)
    {
        bool cleaned = true;
        Task? consumer;
        TelegramAccountSession? session;
        lock (_stateLock)
        {
            _accepting = false;
            _queue?.Writer.TryComplete();
            consumer = _consumer;
            session = _session;
        }
        if (consumer is not null)
        {
            try
            {
                await consumer.WaitAsync(_options.DrainTimeout, cancellationToken);
                lock (_stateLock) _drainCompleted = !_drainAborted;
            }
            catch (TimeoutException)
            {
                lock (_stateLock)
                {
                    _drainCompleted = false;
                    _drainAborted = true;
                    SetErrorLocked(TelegramErrorCategory.ShutdownTimeout);
                }
                _consumerCancellation?.Cancel();
                cleaned = false;
            }
        }
        if (session is not null)
        {
            try
            {
                await session.CloseAsync(cancellationToken);
                lock (_stateLock) _session = null;
            }
            catch (TelegramBoundaryException failure)
            {
                RecordFailure(failure);
                cleaned = false;
                if (session.Closed) lock (_stateLock) _session = null;
            }
        }
        lock (_stateLock)
        {
            if (_consumer is null || _consumer.IsCompleted)
            {
                if (!_drainCompleted && _queue is not null)
                    _possiblyLost += _queue.Reader.Count;
                _consumer = null;
                _consumerCancellation?.Dispose();
                _consumerCancellation = null;
                _queue = null;
                _snapshot = [];
            }
            if (consumer is null) _drainCompleted = true;
            return cleaned && _consumer is null && _session is null;
        }
    }

    private async Task ConsumeAsync(ChannelReader<TelegramChannelPost> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TelegramChannelPost post in reader.ReadAllAsync(cancellationToken))
            {
                // Prompt 1 intermediate commit only accounts for the accepted publication.
                _admission.Add(1, new KeyValuePair<string, object?>("outcome", "consumed"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_stateLock) { _drainCompleted = false; _drainAborted = true; }
        }
        catch (Exception)
        {
            lock (_stateLock) _drainAborted = true;
            SignalFault(TelegramErrorCategory.Unexpected);
        }
    }

    private void HandleUpdate(Update update)
    {
        DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
        _received.Add(1);
        lock (_stateLock)
        {
            if (!_accepting || _queue is null) { _admission.Add(1, new KeyValuePair<string, object?>("outcome", "inactive")); return; }
            TelegramChannelPost? post = TelegramUpdateHandler.Convert(update, _snapshot, _readyAtUtc, receivedAtUtc, _logger);
            if (post is null) { _admission.Add(1, new KeyValuePair<string, object?>("outcome", "filtered")); return; }
            _lastReceivedAtUtc = receivedAtUtc;
            if (_queue.Writer.TryWrite(post))
            {
                _accepted++;
                _admission.Add(1, new KeyValuePair<string, object?>("outcome", "accepted"));
                return;
            }
            _overflow++;
            _possiblyLost++;
            _admission.Add(1, new KeyValuePair<string, object?>("outcome", "overflow"));
            TelegramLog.PublicationFailure(_logger, post.ChannelId, post.MessageId, TelegramErrorCategory.QueueOverflow);
            SignalFault(TelegramErrorCategory.QueueOverflow);
        }
    }

    private void SignalFault(TelegramErrorCategory category)
    {
        lock (_stateLock)
        {
            if (_state is TelegramReceiverState.Stopped or TelegramReceiverState.Disabled) return;
            _accepting = false;
            _queue?.Writer.TryComplete();
            SetErrorLocked(category);
            SetStateLocked(TelegramReceiverState.Error);
            if (_faultSignal.CurrentCount == 0) _faultSignal.Release();
        }
    }

    private void RecordFailure(TelegramBoundaryException failure)
    {
        lock (_stateLock)
        {
            SetErrorLocked(failure.Category);
            SetStateLocked(TelegramReceiverState.Error);
        }
        TelegramLog.Failure(_logger, failure.Operation, failure.Category);
    }

    private void SetErrorLocked(TelegramErrorCategory category)
    {
        _lastErrorCode = category;
        _lastError = category.ToString();
        TelegramLog.Failure(_logger, "receiver", category);
    }

    private void SetStateLocked(TelegramReceiverState state)
    {
        _state = state;
        _transitions.Add(1, new KeyValuePair<string, object?>("state", state.ToString()));
        TelegramLog.State(_logger, state);
    }

    internal async Task WatchFaultsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _faultSignal.WaitAsync(cancellationToken);
            if (!await _commands.WaitAsync(_options.CommandWaitTimeout, cancellationToken))
            {
                lock (_stateLock) if (_faultSignal.CurrentCount == 0) _faultSignal.Release();
                continue;
            }
            try
            {
                lock (_stateLock) if (_state != TelegramReceiverState.Error) continue;
                await CleanupAsync(CancellationToken.None);
            }
            finally { _commands.Release(); }
        }
    }

    internal void SetBotState(string state) { lock (_stateLock) _botState = state; }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;
        if (!await _commands.WaitAsync(_options.CommandWaitTimeout, cancellationToken)) return;
        try { await EnsureCatalogAsync(cancellationToken); }
        catch (TelegramStorageException failure) { RecordFailure(failure); }
        finally { _commands.Release(); }
    }

    private async Task EnsureCatalogAsync(CancellationToken cancellationToken)
    {
        if (_catalogLoaded) return;
        string[] usernames = await _catalog.LoadAsync(cancellationToken);
        lock (_stateLock) { _usernames = usernames; _catalogLoaded = true; }
    }

    /// <summary>Checks a currently accessible broadcast channel, persists, then replaces admission.</summary>
    public async Task<TelegramReceiverCommandResult> AddChannelAsync(string username, CancellationToken cancellationToken)
    {
        if (!TelegramChannelCatalog.TryNormalize(username, out string normalized)) return new(TelegramCommandOutcome.InvalidInput);
        if (!await _commands.WaitAsync(_options.CommandWaitTimeout, cancellationToken)) return new(TelegramCommandOutcome.Conflict);
        try
        {
            TelegramAccountSession? session;
            lock (_stateLock)
            {
                foreach (string existing in _usernames)
                    if (existing == normalized) return new(TelegramCommandOutcome.AlreadyApplied);
                session = _session;
                if (!_accepting || session is null || !session.Authorized) return new(TelegramCommandOutcome.Unavailable);
                if (_usernames.Length >= _options.MaxChannels) return new(TelegramCommandOutcome.Conflict, null, "Channel limit reached.");
            }
            string[] next = new string[_usernames.Length + 1];
            Array.Copy(_usernames, next, _usernames.Length);
            next[^1] = normalized;
            TelegramAccountSession.ChannelResolution resolution;
            try { resolution = await session.ResolveChannelsAsync(next, cancellationToken); }
            catch (TelegramSourceException failure)
            {
                // An inaccessible new channel is a normal refusal, preserving the current admission.
                TelegramLog.Failure(_logger, failure.Operation, failure.Category);
                lock (_stateLock) { _lastErrorCode = failure.Category; _lastError = failure.Category.ToString(); }
                return new(TelegramCommandOutcome.Unavailable, failure.Category);
            }
            if (resolution.UnavailableUsername is not null)
                return new(TelegramCommandOutcome.Unavailable, TelegramErrorCategory.SourceChannelUnavailable,
                    "SourceChannelUnavailable: @" + resolution.UnavailableUsername);
            await _catalog.SaveAsync(next, cancellationToken);
            lock (_stateLock) { _usernames = next; _snapshot = resolution.Channels; }
            return new(TelegramCommandOutcome.Applied);
        }
        catch (TelegramStorageException failure)
        {
            lock (_stateLock) { _lastErrorCode = failure.Category; _lastError = failure.Category.ToString(); }
            return new(TelegramCommandOutcome.Failed, failure.Category);
        }
        finally { _commands.Release(); }
    }

    /// <summary>Atomically persists removal, leaving previously accepted publications in the queue.</summary>
    public async Task<TelegramReceiverCommandResult> RemoveChannelAsync(string username, CancellationToken cancellationToken)
    {
        if (!TelegramChannelCatalog.TryNormalize(username, out string normalized)) return new(TelegramCommandOutcome.InvalidInput);
        if (!await _commands.WaitAsync(_options.CommandWaitTimeout, cancellationToken)) return new(TelegramCommandOutcome.Conflict);
        try
        {
            if (!_options.Enabled) return new(TelegramCommandOutcome.Unavailable);
            await EnsureCatalogAsync(cancellationToken);
            int index = Array.IndexOf(_usernames, normalized);
            if (index < 0) return new(TelegramCommandOutcome.AlreadyApplied);
            string[] next = new string[_usernames.Length - 1];
            Array.Copy(_usernames, 0, next, 0, index);
            Array.Copy(_usernames, index + 1, next, index, next.Length - index);
            Dictionary<long, TelegramUpdateHandler.ChannelInfo> snapshot = [];
            lock (_stateLock)
                foreach (KeyValuePair<long, TelegramUpdateHandler.ChannelInfo> entry in _snapshot)
                    if (entry.Value.Username != normalized) snapshot.Add(entry.Key, entry.Value);
            await _catalog.SaveAsync(next, cancellationToken);
            lock (_stateLock) { _usernames = next; _snapshot = snapshot; }
            return new(TelegramCommandOutcome.Applied);
        }
        catch (TelegramStorageException failure)
        {
            lock (_stateLock) { _lastErrorCode = failure.Category; _lastError = failure.Category.ToString(); }
            return new(TelegramCommandOutcome.Failed, failure.Category);
        }
        finally { _commands.Release(); }
    }

    /// <summary>The host calls this after the hosted runtime has completed shutdown.</summary>
    public void Dispose()
    {
        _commands.Dispose();
        _faultSignal.Dispose();
        _consumerCancellation?.Dispose();
    }
}
