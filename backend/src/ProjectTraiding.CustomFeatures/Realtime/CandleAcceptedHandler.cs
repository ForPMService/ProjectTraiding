using ProjectTraiding.Moex.Realtime.Subscriptions;

namespace ProjectTraiding.CustomFeatures.Realtime;

/// <summary>Bridges Wolverine's fixed handler to the currently registered candle consumers.</summary>
public sealed class CandleAcceptedHandler
{
    private readonly CandleSubscriptionRegistry _subscriptions;
    private readonly ILogger<CandleAcceptedHandler> _logger;

    public CandleAcceptedHandler(
        CandleSubscriptionRegistry subscriptions,
        ILogger<CandleAcceptedHandler> logger)
    {
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public async ValueTask HandleAsync(
        CandleAccepted message,
        CancellationToken cancellationToken)
    {
        CandleSubscriptionSnapshot snapshot = _subscriptions.CaptureSnapshot();
        if (snapshot.Count == 0)
            return;

        for (int i = 0; i < snapshot.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CandleAcceptedSubscription subscription = snapshot[i];
            if (!subscription.IsActive)
                continue;

            try
            {
                await subscription.InvokeAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Candle consumer cancelled: registration={RegistrationId}, secid={Secid}, " +
                    "market={Market}, interval={Interval}, begin={Begin}.",
                    subscription.RegistrationId,
                    message.Candle.Secid,
                    message.Market,
                    message.Interval,
                    message.Candle.Begin);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Candle consumer failed: registration={RegistrationId}, secid={Secid}, " +
                    "market={Market}, interval={Interval}, begin={Begin}.",
                    subscription.RegistrationId,
                    message.Candle.Secid,
                    message.Market,
                    message.Interval,
                    message.Candle.Begin);
            }
        }
    }
}
