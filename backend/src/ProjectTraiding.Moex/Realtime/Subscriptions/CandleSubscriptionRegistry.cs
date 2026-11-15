using System.Threading;

namespace ProjectTraiding.Moex.Realtime.Subscriptions;

/// <summary>Tracks the live application consumers of accepted candle updates.</summary>
public sealed class CandleSubscriptionRegistry
{
    private readonly object _gate = new();
    private CandleAcceptedSubscription[] _subscriptions = Array.Empty<CandleAcceptedSubscription>();
    private long _nextRegistrationId;

    /// <summary>True when at least one application consumer is registered.</summary>
    public bool HasSubscribers => Volatile.Read(ref _subscriptions).Length != 0;

    /// <summary>Captures the current registrations without allocating or taking a lock.</summary>
    public CandleSubscriptionSnapshot CaptureSnapshot()
        => new(Volatile.Read(ref _subscriptions));

    /// <summary>Registers one live consumer and returns an idempotent handle for that registration.</summary>
    /// <remarks>
    /// Dispose the registration before disposing a scope captured by its callback, and keep that
    /// scope alive until any callback that already started has completed. Disposal prevents later
    /// snapshots from invoking the callback but does not abort an invocation already in progress.
    /// </remarks>
    public IDisposable Subscribe(
        Func<CandleAccepted, CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            CandleAcceptedSubscription[] current = Volatile.Read(ref _subscriptions);
            long registrationId = checked(++_nextRegistrationId);
            CandleAcceptedSubscription subscription = new(registrationId, callback);
            CandleAcceptedSubscription[] updated = new CandleAcceptedSubscription[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[^1] = subscription;
            Volatile.Write(ref _subscriptions, updated);
            return new SubscriptionRegistration(this, registrationId);
        }
    }

    private void Unsubscribe(long registrationId)
    {
        lock (_gate)
        {
            CandleAcceptedSubscription[] current = Volatile.Read(ref _subscriptions);
            int index = -1;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].RegistrationId == registrationId)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
                return;

            CandleAcceptedSubscription removed = current[index];
            CandleAcceptedSubscription[] updated = current.Length == 1
                ? Array.Empty<CandleAcceptedSubscription>()
                : new CandleAcceptedSubscription[current.Length - 1];

            if (current.Length > 1)
            {
                if (index > 0)
                    Array.Copy(current, 0, updated, 0, index);
                if (index < current.Length - 1)
                    Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            }

            Volatile.Write(ref _subscriptions, updated);
            removed.Deactivate();
        }
    }

    private sealed class SubscriptionRegistration : IDisposable
    {
        private CandleSubscriptionRegistry? _registry;
        private readonly long _registrationId;

        public SubscriptionRegistration(CandleSubscriptionRegistry registry, long registrationId)
        {
            _registry = registry;
            _registrationId = registrationId;
        }

        public void Dispose()
        {
            CandleSubscriptionRegistry? registry = Interlocked.Exchange(ref _registry, null);
            registry?.Unsubscribe(_registrationId);
        }
    }
}

/// <summary>One immutable snapshot of the registry's active subscriptions.</summary>
public readonly struct CandleSubscriptionSnapshot
{
    private readonly CandleAcceptedSubscription[]? _subscriptions;

    internal CandleSubscriptionSnapshot(CandleAcceptedSubscription[] subscriptions)
        => _subscriptions = subscriptions;

    public int Count => _subscriptions?.Length ?? 0;

    public CandleAcceptedSubscription this[int index]
        => (_subscriptions ?? Array.Empty<CandleAcceptedSubscription>())[index];
}

/// <summary>A single callback registration held by a subscription snapshot.</summary>
public sealed class CandleAcceptedSubscription
{
    private readonly Func<CandleAccepted, CancellationToken, ValueTask> _callback;
    private int _isActive = 1;

    internal CandleAcceptedSubscription(
        long registrationId,
        Func<CandleAccepted, CancellationToken, ValueTask> callback)
    {
        RegistrationId = registrationId;
        _callback = callback;
    }

    public long RegistrationId { get; }

    public bool IsActive => Volatile.Read(ref _isActive) != 0;

    public ValueTask InvokeAsync(CandleAccepted message, CancellationToken cancellationToken)
        => _callback(message, cancellationToken);

    internal void Deactivate()
        => Volatile.Write(ref _isActive, 0);
}
