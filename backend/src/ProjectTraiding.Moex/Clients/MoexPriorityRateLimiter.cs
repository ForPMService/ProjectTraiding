using ProjectTraiding.Moex.Infrastructure.Telemetry;
using System.Threading.RateLimiting;

namespace ProjectTraiding.Moex.Clients;

/// <summary>
/// Держит ограниченные FIFO-очереди MOEX и выбирает запрос после получения общего жетона.
/// Асинхронный жетон ожидает только диспетчер; быстрый путь использует тот же limiter под _gate.
/// </summary>
public sealed class MoexPriorityRateLimiter : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly RateLimiter _rateLimiter;
    private readonly int _queueLimit;
    private readonly LinkedList<PendingRequest> _realtimeQueue = new();
    private readonly LinkedList<PendingRequest> _historyQueue = new();
    private readonly SemaphoreSlim _wakeDispatcher = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _dispatcherTask;

    private CancellationTokenSource? _permitAcquireCancellation;
    private Exception? _dispatcherFailure;
    private TaskCompletionSource<bool>? _disposeCompletion;
    private bool _disposed;

    public MoexPriorityRateLimiter(RateLimiter rateLimiter, int queueLimit)
    {
        ArgumentNullException.ThrowIfNull(rateLimiter);
        if (queueLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(queueLimit));

        _rateLimiter = rateLimiter;
        _queueLimit = queueLimit;
        _dispatcherTask = RunDispatcherAsync(_shutdown.Token);
    }

    /// <summary>
    /// Быстрый путь работает только при пустой предметной очереди и без ожидающего
    /// разрешения диспетчера. Это не даёт History обойти уже ожидающий Realtime.
    /// </summary>
    public bool AttemptAcquire(string flow)
    {
        lock (_gate)
        {
            ThrowIfUnavailable();
            if (HasPendingRequestsLocked() || _permitAcquireCancellation is not null)
                return false;

            using RateLimitLease lease = _rateLimiter.AttemptAcquire(permitCount: 1);
            return lease.IsAcquired;
        }
    }

    /// <summary>
    /// Берёт жетон сразу либо ставит запрос во внешнюю приоритетную очередь.
    /// При заполнении History вытесняется её последний ожидающий запрос для Realtime.
    /// </summary>
    public ValueTask<bool> AcquireAsync(string flow, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<bool>(cancellationToken);

        bool isRealtime = flow == MoexFlows.Realtime;
        PendingRequest? pendingRequest = null;
        PendingRequest? displacedHistory = null;

        lock (_gate)
        {
            ThrowIfUnavailable();
            if (!HasPendingRequestsLocked() && _permitAcquireCancellation is null)
            {
                using RateLimitLease lease = _rateLimiter.AttemptAcquire(permitCount: 1);
                if (lease.IsAcquired)
                    return ValueTask.FromResult(true);
            }

            if (QueuedCountLocked() >= _queueLimit)
            {
                if (!isRealtime || _historyQueue.Last is null)
                    return ValueTask.FromResult(false);

                displacedHistory = RemoveLastLocked(_historyQueue);
            }

            LinkedList<PendingRequest> queue = isRealtime
                ? _realtimeQueue
                : _historyQueue;
            pendingRequest = EnqueueLocked(queue, cancellationToken);
        }

        if (displacedHistory is not null)
            CompleteQueueFull(displacedHistory);

        WakeDispatcher();
        return new ValueTask<bool>(pendingRequest.Completion.Task);
    }

    private PendingRequest EnqueueLocked(
        LinkedList<PendingRequest> queue,
        CancellationToken cancellationToken)
    {
        PendingRequest pendingRequest = new(this, cancellationToken);
        pendingRequest.Node = queue.AddLast(pendingRequest);
        pendingRequest.CancellationRegistration = cancellationToken.Register(
            static state =>
            {
                PendingRequest request = (PendingRequest)state!;
                request.Owner.CancelPendingRequest(request);
            },
            pendingRequest);

        if (pendingRequest.Node is null)
            pendingRequest.CancellationRegistration.Unregister();

        return pendingRequest;
    }

    private void CancelPendingRequest(PendingRequest pendingRequest)
    {
        bool removed;
        CancellationTokenSource? permitAcquireCancellation = null;

        lock (_gate)
        {
            removed = pendingRequest.Node is not null;
            if (pendingRequest.Node is not null)
            {
                pendingRequest.Node.List?.Remove(pendingRequest.Node);
                pendingRequest.Node = null;
            }

            if (removed && !HasPendingRequestsLocked())
                permitAcquireCancellation = _permitAcquireCancellation;
        }

        if (!removed)
            return;

        pendingRequest.Completion.TrySetCanceled(pendingRequest.CancellationToken);
        pendingRequest.CancellationRegistration.Unregister();
        CancelSafely(permitAcquireCancellation);
        WakeDispatcher();
    }

    private async Task RunDispatcherAsync(CancellationToken shutdownToken)
    {
        try
        {
            while (true)
            {
                await _wakeDispatcher.WaitAsync(shutdownToken).ConfigureAwait(false);

                while (true)
                {
                    CancellationTokenSource permitAcquireCancellation;
                    lock (_gate)
                    {
                        if (_disposed)
                            return;
                        if (!HasPendingRequestsLocked())
                            break;

                        permitAcquireCancellation =
                            CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                        _permitAcquireCancellation = permitAcquireCancellation;
                    }

                    try
                    {
                        using RateLimitLease lease = await _rateLimiter.AcquireAsync(
                            permitCount: 1,
                            cancellationToken: permitAcquireCancellation.Token).ConfigureAwait(false);

                        if (!lease.IsAcquired)
                        {
                            throw new InvalidOperationException(
                                "MOEX rate limiter rejected its single dispatcher waiter.");
                        }

                        PendingRequest? selectedRequest = null;
                        List<PendingRequest>? cancelledRequests = null;
                        lock (_gate)
                        {
                            ClearPermitAcquireCancellationLocked(permitAcquireCancellation);
                            if (!_disposed)
                            {
                                selectedRequest = TakeNextPendingLocked(ref cancelledRequests);
                            }
                        }

                        CompleteCancelled(cancelledRequests);
                        if (selectedRequest is not null)
                        {
                            selectedRequest.CancellationRegistration.Unregister();
                            selectedRequest.Completion.TrySetResult(true);
                        }
                    }
                    catch (OperationCanceledException) when (
                        !shutdownToken.IsCancellationRequested
                        && permitAcquireCancellation.IsCancellationRequested)
                    {
                        // Все внешние ожидания отменились; не держим лишний внутренний waiter.
                    }
                    finally
                    {
                        lock (_gate)
                            ClearPermitAcquireCancellationLocked(permitAcquireCancellation);
                        permitAcquireCancellation.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            // Штатное завершение: DisposeAsync отменяет внутреннее ожидание и ждёт этот цикл.
        }
        catch (Exception exception)
        {
            FailPendingRequests(exception);
            throw;
        }
    }

    private PendingRequest? TakeNextPendingLocked(
        ref List<PendingRequest>? cancelledRequests)
    {
        while (_realtimeQueue.Count > 0 || _historyQueue.Count > 0)
        {
            LinkedList<PendingRequest> queue = _realtimeQueue.Count > 0
                ? _realtimeQueue
                : _historyQueue;
            PendingRequest pendingRequest = queue.First!.Value;
            queue.RemoveFirst();
            pendingRequest.Node = null;

            if (pendingRequest.CancellationToken.IsCancellationRequested)
            {
                (cancelledRequests ??= new List<PendingRequest>()).Add(pendingRequest);
                continue;
            }

            return pendingRequest;
        }

        return null;
    }

    private PendingRequest RemoveLastLocked(LinkedList<PendingRequest> queue)
    {
        PendingRequest pendingRequest = queue.Last!.Value;
        queue.RemoveLast();
        pendingRequest.Node = null;
        return pendingRequest;
    }

    private void FailPendingRequests(Exception exception)
    {
        List<PendingRequest>? pendingRequests = null;
        CancellationTokenSource? permitAcquireCancellation;

        lock (_gate)
        {
            _dispatcherFailure = exception;
            MoveQueuedRequestsLocked(_realtimeQueue, ref pendingRequests);
            MoveQueuedRequestsLocked(_historyQueue, ref pendingRequests);
            permitAcquireCancellation = _permitAcquireCancellation;
        }

        CancelSafely(permitAcquireCancellation);
        if (pendingRequests is null)
            return;

        foreach (PendingRequest pendingRequest in pendingRequests)
        {
            pendingRequest.CancellationRegistration.Unregister();
            pendingRequest.Completion.TrySetException(exception);
        }
    }

    private void ThrowIfUnavailable()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MoexPriorityRateLimiter));
        if (_dispatcherFailure is not null)
        {
            throw new InvalidOperationException(
                "MOEX priority dispatcher stopped after an unexpected failure.",
                _dispatcherFailure);
        }
    }

    private void WakeDispatcher()
    {
        try
        {
            _wakeDispatcher.Release();
        }
        catch (SemaphoreFullException)
        {
            // One signal is enough; the dispatcher checks both queues under _gate.
        }
        catch (ObjectDisposedException)
        {
            // A cancellation callback can finish after asynchronous disposal begins.
        }
    }

    private static void CompleteQueueFull(PendingRequest pendingRequest)
    {
        pendingRequest.CancellationRegistration.Unregister();
        pendingRequest.Completion.TrySetResult(false);
    }

    private static void CompleteCancelled(List<PendingRequest>? pendingRequests)
    {
        if (pendingRequests is null)
            return;

        foreach (PendingRequest pendingRequest in pendingRequests)
        {
            pendingRequest.CancellationRegistration.Unregister();
            pendingRequest.Completion.TrySetCanceled(pendingRequest.CancellationToken);
        }
    }

    private static void CompleteDisposed(List<PendingRequest>? pendingRequests)
    {
        if (pendingRequests is null)
            return;

        foreach (PendingRequest pendingRequest in pendingRequests)
        {
            pendingRequest.CancellationRegistration.Unregister();
            pendingRequest.Completion.TrySetException(
                new ObjectDisposedException(nameof(MoexPriorityRateLimiter)));
        }
    }

    private bool HasPendingRequestsLocked()
        => _realtimeQueue.Count > 0 || _historyQueue.Count > 0;

    private int QueuedCountLocked()
        => _realtimeQueue.Count + _historyQueue.Count;

    private void ClearPermitAcquireCancellationLocked(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_permitAcquireCancellation, cancellation))
            _permitAcquireCancellation = null;
    }

    private static void MoveQueuedRequestsLocked(
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

    private static Exception? CancelSafely(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return null;

        try
        {
            cancellation.Cancel();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public ValueTask DisposeAsync()
    {
        List<PendingRequest>? pendingRequests = null;
        CancellationTokenSource? permitAcquireCancellation;
        TaskCompletionSource<bool> completion;

        lock (_gate)
        {
            if (_disposeCompletion is not null)
                return new ValueTask(_disposeCompletion.Task);

            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeCompletion = completion;
            _disposed = true;
            MoveQueuedRequestsLocked(_realtimeQueue, ref pendingRequests);
            MoveQueuedRequestsLocked(_historyQueue, ref pendingRequests);
            permitAcquireCancellation = _permitAcquireCancellation;
        }

        Exception? cancellationFailure = CancelSafely(permitAcquireCancellation);
        try
        {
            _shutdown.Cancel();
        }
        catch (Exception exception)
        {
            cancellationFailure ??= exception;
        }

        CompleteDisposed(pendingRequests);
        WakeDispatcher();
        _ = FinishDisposalAsync(completion, cancellationFailure);
        return new ValueTask(completion.Task);
    }

    private async Task FinishDisposalAsync(
        TaskCompletionSource<bool> completion,
        Exception? initialFailure)
    {
        Exception? failure = initialFailure;
        try
        {
            await _dispatcherTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        try
        {
            _wakeDispatcher.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        try
        {
            _shutdown.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is null)
            completion.TrySetResult(true);
        else
            completion.TrySetException(failure);
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
}
