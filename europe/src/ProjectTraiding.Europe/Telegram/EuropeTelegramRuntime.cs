using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>One session and one bounded worker for forwarding original publications.</summary>
public sealed class EuropeTelegramRuntime(TelegramOptions options) : BackgroundService
{
    private readonly object _admission = new();
    private readonly CancellationTokenSource _forwardingLifetime = new();
    private Channel<TelegramChannelPost>? _forwarding;
    private CancellationToken _stoppingToken;

    /// <summary>
    /// Immediate publication output for the next LLM stage, independent of forwarding completion.
    /// Subscribers must accept promptly; asynchronous LLM execution is not implemented here.
    /// </summary>
    public event Action<TelegramChannelPost>? PublicationReceived;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        _stoppingToken = stoppingToken;
        _forwarding = Channel.CreateBounded<TelegramChannelPost>(new BoundedChannelOptions(options.ForwardQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
        using CancellationTokenRegistration stopping = stoppingToken.Register(CompleteForwarding);
        await using TelegramAccountSession session = new(options, Publish, Fail);
        try
        {
            await session.ConnectAsync(stoppingToken);
            // Host cancellation closes admission; accepted originals drain until its shutdown deadline.
            await foreach (TelegramChannelPost post in _forwarding.Reader.ReadAllAsync(_forwardingLifetime.Token))
                await session.ForwardAsync(post, _forwardingLifetime.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested
            && !_forwardingLifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) when (_forwardingLifetime.IsCancellationRequested)
        {
            throw new TimeoutException("Telegram forwarding did not finish before the host shutdown deadline.");
        }
        finally { CompleteForwarding(); }
    }

    private void Publish(TelegramChannelPost post)
    {
        lock (_admission)
        {
            if (_stoppingToken.IsCancellationRequested) return;
            if (!_forwarding!.Writer.TryWrite(post))
            {
                _forwarding.Writer.TryComplete(new InvalidOperationException("Telegram forwarding buffer is full or closed."));
                return;
            }
        }
        // No forwarding await, Task.Run or LLM queue is introduced on this output.
        PublicationReceived?.Invoke(post);
    }

    private void Fail(Exception exception) => _forwarding!.Writer.TryComplete(exception);

    private void CompleteForwarding()
    {
        lock (_admission) _forwarding?.Writer.TryComplete();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration deadline = cancellationToken.Register(_forwardingLifetime.Cancel);
        await base.StopAsync(cancellationToken);
        if (ExecuteTask is { IsCompleted: false })
            throw new TimeoutException("Telegram shutdown did not finish; accepted originals may remain unforwarded.");
    }

    public override void Dispose()
    {
        base.Dispose();
        _forwardingLifetime.Dispose();
    }
}
