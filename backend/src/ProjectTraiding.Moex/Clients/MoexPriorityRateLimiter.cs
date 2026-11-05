using ProjectTraiding.Moex.Infrastructure.Telemetry;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Clients;

/// <summary>
/// Один общий токеновый бюджет MOEX с FIFO-очередями, в которых realtime обслуживается
/// раньше history. Выдача жетона и выбор очереди сериализованы одним lock, в том числе
/// для синхронной быстрой попытки.
/// </summary>
public sealed class MoexPriorityRateLimiter : IDisposable
{
    private readonly object _gate = new();
    private readonly int _permitLimit;
    private readonly int _queueLimit;
    private readonly LinkedList<PendingRequest> _realtimeQueue = new();
    private readonly LinkedList<PendingRequest> _historyQueue = new();
    private readonly SemaphoreSlim _wakeDispatcher = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _dispatcherTask;

    private int _availablePermits;
    private long _nextReplenishmentTimestamp;
    private bool _disposed;

    public MoexPriorityRateLimiter(int permitsPerSecond, int queueLimit)
    {
        if (permitsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(permitsPerSecond));
        if (queueLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(queueLimit));

        _permitLimit = permitsPerSecond;
        _availablePermits = permitsPerSecond;
        _queueLimit = queueLimit;
        _nextReplenishmentTimestamp = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        _dispatcherTask = RunDispatcherAsync(_shutdown.Token);
    }

    /// <summary>Пробует взять жетон, не ожидая. Ожидающие запросы сохраняют приоритет.</summary>
    public bool AttemptAcquire(string flow)
    {
        List<PendingCompletion>? completions = null;
        bool acquired;
        bool hasPendingRequests;

        lock (_gate)
        {
            ThrowIfDisposed();
            ReplenishLocked(Stopwatch.GetTimestamp());

            if (flow == MoexFlows.Realtime)
            {
                DispatchQueueLocked(_realtimeQueue, ref completions);
                acquired = TryConsumePermitLocked();
                if (_availablePermits > 0)
                    DispatchAllQueuesLocked(ref completions);
            }
            else
            {
                DispatchAllQueuesLocked(ref completions);
                acquired = _realtimeQueue.Count == 0
                    && _historyQueue.Count == 0
                    && TryConsumePermitLocked();
            }

            hasPendingRequests = HasPendingRequestsLocked();
        }

        Complete(completions);
        if (hasPendingRequests)
            WakeDispatcher();
        return acquired;
    }

    /// <summary>
    /// Берёт жетон либо ждёт его в ограниченной очереди. Отмена снимает запрос с очереди.
    /// </summary>
    public ValueTask<bool> AcquireAsync(string flow, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<bool>(cancellationToken);

        List<PendingCompletion>? completions = null;
        PendingRequest? pendingRequest = null;
        bool acquired;
        bool queueFull = false;
        bool hasPendingRequests;

        lock (_gate)
        {
            ThrowIfDisposed();
            ReplenishLocked(Stopwatch.GetTimestamp());

            if (flow == MoexFlows.Realtime)
            {
                DispatchQueueLocked(_realtimeQueue, ref completions);
                acquired = TryConsumePermitLocked();
                if (!acquired)
                    pendingRequest = QueueRequestLocked(_realtimeQueue, cancellationToken);
                if (_availablePermits > 0)
                    DispatchAllQueuesLocked(ref completions);
            }
            else
            {
                DispatchAllQueuesLocked(ref completions);
                acquired = _realtimeQueue.Count == 0
                    && _historyQueue.Count == 0
                    && TryConsumePermitLocked();
                if (!acquired)
                    pendingRequest = QueueRequestLocked(_historyQueue, cancellationToken);
            }

            queueFull = !acquired && pendingRequest is null;
            hasPendingRequests = HasPendingRequestsLocked();
        }

        Complete(completions);
        if (hasPendingRequests)
            WakeDispatcher();

        if (acquired)
            return ValueTask.FromResult(true);
        if (queueFull)
            return ValueTask.FromResult(false);

        return new ValueTask<bool>(pendingRequest!.Completion.Task);
    }

    private PendingRequest? QueueRequestLocked(
        LinkedList<PendingRequest> queue,
        CancellationToken cancellationToken)
    {
        if (_realtimeQueue.Count + _historyQueue.Count >= _queueLimit)
            return null;

        PendingRequest pendingRequest = new(this, cancellationToken);
        pendingRequest.Node = queue.AddLast(pendingRequest);
        pendingRequest.CancellationRegistration = cancellationToken.Register(
            static state =>
            {
                PendingRequest request = (PendingRequest)state!;
                request.Owner.CancelPendingRequest(request);
            },
            pendingRequest);
        return pendingRequest;
    }

    private void CancelPendingRequest(PendingRequest pendingRequest)
    {
        bool removed;
        lock (_gate)
        {
            removed = pendingRequest.Node is not null;
            if (pendingRequest.Node is not null)
            {
                pendingRequest.Node.List?.Remove(pendingRequest.Node);
                pendingRequest.Node = null;
            }
        }

        if (removed)
        {
            pendingRequest.Completion.TrySetCanceled(pendingRequest.CancellationToken);
            WakeDispatcher();
        }
    }

    private async Task RunDispatcherAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                List<PendingCompletion>? completions = null;
                TimeSpan nextWait;
                lock (_gate)
                {
                    if (_disposed)
                        return;

                    ReplenishLocked(Stopwatch.GetTimestamp());
                    DispatchAllQueuesLocked(ref completions);
                    nextWait = HasPendingRequestsLocked()
                        ? GetTimeToReplenishmentLocked()
                        : Timeout.InfiniteTimeSpan;
                }

                Complete(completions);

                if (nextWait == Timeout.InfiniteTimeSpan)
                {
                    await _wakeDispatcher.WaitAsync(cancellationToken);
                }
                else
                {
                    await _wakeDispatcher.WaitAsync(nextWait, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposal stops the sole dispatcher after queued requests are completed.
        }
    }

    private void DispatchAllQueuesLocked(ref List<PendingCompletion>? completions)
    {
        DispatchQueueLocked(_realtimeQueue, ref completions);
        DispatchQueueLocked(_historyQueue, ref completions);
    }

    private void DispatchQueueLocked(
        LinkedList<PendingRequest> queue,
        ref List<PendingCompletion>? completions)
    {
        while (_availablePermits > 0 && queue.First is not null)
        {
            PendingRequest pendingRequest = queue.First.Value;
            queue.RemoveFirst();
            pendingRequest.Node = null;

            if (pendingRequest.CancellationToken.IsCancellationRequested)
            {
                AddCompletion(
                    ref completions,
                    new PendingCompletion(pendingRequest, Acquired: false));
                continue;
            }

            _availablePermits--;
            AddCompletion(
                ref completions,
                new PendingCompletion(pendingRequest, Acquired: true));
        }
    }

    private void ReplenishLocked(long timestamp)
    {
        if (timestamp < _nextReplenishmentTimestamp)
            return;

        long elapsedPeriods =
            (timestamp - _nextReplenishmentTimestamp) / Stopwatch.Frequency + 1;
        long replenishedPermits = elapsedPeriods * _permitLimit;
        _availablePermits = (int)Math.Min(
            _permitLimit,
            _availablePermits + replenishedPermits);
        _nextReplenishmentTimestamp += elapsedPeriods * Stopwatch.Frequency;
    }

    private TimeSpan GetTimeToReplenishmentLocked()
    {
        long remainingTicks = _nextReplenishmentTimestamp - Stopwatch.GetTimestamp();
        return remainingTicks <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }

    private bool TryConsumePermitLocked()
    {
        if (_availablePermits == 0)
            return false;

        _availablePermits--;
        return true;
    }

    private bool HasPendingRequestsLocked()
        => _realtimeQueue.Count > 0 || _historyQueue.Count > 0;

    private void WakeDispatcher()
    {
        try
        {
            _wakeDispatcher.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending signal is enough; the dispatcher reads all queue state under _gate.
        }
        catch (ObjectDisposedException)
        {
            // A cancellation callback can finish after service disposal begins.
        }
    }

    private static void AddCompletion(
        ref List<PendingCompletion>? completions,
        PendingCompletion completion)
    {
        completions ??= new List<PendingCompletion>();
        completions.Add(completion);
    }

    private static void Complete(List<PendingCompletion>? completions)
    {
        if (completions is null)
            return;

        foreach (PendingCompletion completion in completions)
        {
            PendingRequest pendingRequest = completion.Request;
            pendingRequest.CancellationRegistration.Unregister();
            if (completion.Acquired)
                pendingRequest.Completion.TrySetResult(true);
            else
                pendingRequest.Completion.TrySetCanceled(pendingRequest.CancellationToken);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MoexPriorityRateLimiter));
    }

    public void Dispose()
    {
        List<PendingRequest>? pendingRequests = null;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            MoveToDisposalListLocked(_realtimeQueue, ref pendingRequests);
            MoveToDisposalListLocked(_historyQueue, ref pendingRequests);
        }

        _shutdown.Cancel();
        WakeDispatcher();
        if (pendingRequests is not null)
        {
            foreach (PendingRequest pendingRequest in pendingRequests)
            {
                pendingRequest.CancellationRegistration.Unregister();
                pendingRequest.Completion.TrySetException(
                    new ObjectDisposedException(nameof(MoexPriorityRateLimiter)));
            }
        }

        _dispatcherTask.GetAwaiter().GetResult();
        _wakeDispatcher.Dispose();
        _shutdown.Dispose();
    }

    private static void MoveToDisposalListLocked(
        LinkedList<PendingRequest> queue,
        ref List<PendingRequest>? pendingRequests)
    {
        while (queue.First is not null)
        {
            PendingRequest pendingRequest = queue.First.Value;
            queue.RemoveFirst();
            pendingRequest.Node = null;
            (pendingRequests ??= new List<PendingRequest>()).Add(pendingRequest);
        }
    }

    private sealed class PendingRequest
    {
        public PendingRequest(
            MoexPriorityRateLimiter owner,
            CancellationToken cancellationToken)
        {
            Owner = owner;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public MoexPriorityRateLimiter Owner { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<bool> Completion { get; }
        public LinkedListNode<PendingRequest>? Node { get; set; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }

    private readonly record struct PendingCompletion(PendingRequest Request, bool Acquired);
}
