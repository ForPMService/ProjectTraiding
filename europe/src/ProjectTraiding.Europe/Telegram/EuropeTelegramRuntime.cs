using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Принимает публикации и независимо выдаёт их подписчикам и в очередь пересылки.</summary>
public sealed partial class EuropeTelegramRuntime(TelegramOptions options, ILogger<EuropeTelegramRuntime> logger) : BackgroundService
{
    private readonly object _admission = new();
    private readonly CancellationTokenSource _forwardingLifetime = new();
    private Channel<TelegramChannelPost>? _forwarding;
    private CancellationToken _stoppingToken;

    /// <summary>Синхронно передаёт публикацию следующему этапу; подписчик отвечает за собственную неблокирующую очередь.</summary>
    public event Action<TelegramChannelPost>? PublicationReceived;

    /// <summary>Запускает одну сессию и последовательно пересылает принятые оригиналы.</summary>
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
            // При остановке хоста приём новых публикаций закрывается, а очередь дренируется до срока остановки.
            await foreach (TelegramChannelPost post in _forwarding.Reader.ReadAllAsync(_forwardingLifetime.Token))
            {
                try
                {
                    await session.ForwardAsync(post, _forwardingLifetime.Token);
                }
                catch (OperationCanceledException) when (_forwardingLifetime.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Ошибка одного оригинала не должна останавливать приём и обработку следующих публикаций.
                    LogWarning(logger, "Не удалось переслать оригинал Telegram; приём публикаций продолжается.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested
            && !_forwardingLifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) when (_forwardingLifetime.IsCancellationRequested)
        {
            throw new TimeoutException("Telegram forwarding did not finish before the host shutdown deadline.");
        }
        finally { CompleteForwarding(); }
    }

    /// <summary>Неблокирующе ставит оригинал в очередь и всегда вызывает выходное событие при активном приёме.</summary>
    private void Publish(TelegramChannelPost post)
    {
        bool queued;
        lock (_admission)
        {
            if (_stoppingToken.IsCancellationRequested) return;
            queued = _forwarding!.Writer.TryWrite(post);
        }

        if (!queued)
            LogWarning(logger, "Очередь пересылки Telegram заполнена; оригинал не поставлен в очередь.");

        // Событие вызывается вне блокировки и ошибки его подписчика не отменяют уже независимую пересылку.
        try
        {
            PublicationReceived?.Invoke(post);
        }
        catch (Exception)
        {
            LogWarning(logger, "Не удалось передать публикацию подписчику; приём и пересылка продолжаются.");
        }
    }

    /// <summary>Записывает краткое предупреждение стандартным журналом без данных публикации и секретов.</summary>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogWarning(ILogger logger, string message);

    /// <summary>Сохраняет ошибку приёма в завершении очереди, чтобы она осталась видна хосту.</summary>
    private void Fail(Exception exception) => _forwarding!.Writer.TryComplete(exception);

    /// <summary>Закрывает очередь при остановке приёма, сохраняя возможность прочитать уже принятые элементы.</summary>
    private void CompleteForwarding()
    {
        lock (_admission) _forwarding?.Writer.TryComplete();
    }

    /// <summary>Дожидается завершения фоновой службы до установленного срока остановки.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration deadline = cancellationToken.Register(_forwardingLifetime.Cancel);
        await base.StopAsync(cancellationToken);
        if (ExecuteTask is { IsCompleted: false })
            throw new TimeoutException("Telegram shutdown did not finish; accepted originals may remain unforwarded.");
    }

    /// <summary>Освобождает ресурсы фоновой службы.</summary>
    public override void Dispose()
    {
        base.Dispose();
        _forwardingLifetime.Dispose();
    }
}
