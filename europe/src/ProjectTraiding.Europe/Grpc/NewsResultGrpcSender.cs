using System.Text.Json;
using System.Threading.Channels;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Europe.News;

namespace ProjectTraiding.Europe.Grpc;

/// <summary>Буферизует и последовательно передаёт завершённые результаты анализа.</summary>
public sealed partial class NewsResultGrpcSender : BackgroundService
{
    private readonly NewsGrpcOptions _options;
    private readonly NewsExchange.NewsExchangeClient _client;
    private readonly NewsAgentExecutor _executor;
    private readonly ILogger<NewsResultGrpcSender> _logger;
    private readonly Channel<PendingNewsResult> _pendingResults;
    private int _accepting;
    private int _activeSend;
    private int _subscribed;

    /// <summary>Создаёт ограниченный однопотребительский буфер результата.</summary>
    public NewsResultGrpcSender(NewsGrpcOptions options, NewsExchange.NewsExchangeClient client,
        NewsAgentExecutor executor, ILogger<NewsResultGrpcSender> logger)
    {
        _options = options;
        _client = client;
        _executor = executor;
        _logger = logger;
        _pendingResults = Channel.CreateBounded<PendingNewsResult>(new BoundedChannelOptions(options.ResultQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>Устанавливает подписку синхронно перед запуском фонового цикла.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;
        Volatile.Write(ref _accepting, 1);
        _executor.AnalysisCompleted += OnAnalysisCompleted;
        Volatile.Write(ref _subscribed, 1);
        try
        {
            await base.StartAsync(cancellationToken);
        }
        catch
        {
            StopAccepting();
            throw;
        }
    }

    /// <summary>Тестируемая точка входа совпадает с обработчиком события анализатора.</summary>
    internal void AcceptResult(NewsAgentResult result) => OnAnalysisCompleted(result);

    private void OnAnalysisCompleted(NewsAgentResult result)
    {
        if (Volatile.Read(ref _accepting) == 0)
        {
            LogResultLost(_logger, "sender_stopped");
            return;
        }
        PendingNewsResult pending = new(Guid.NewGuid().ToString("N"), result);
        if (!_pendingResults.Writer.TryWrite(pending))
            LogResultLost(_logger, "queue_full");
    }

    /// <summary>Последовательно сериализует и отправляет результаты, не влияя на анализатор.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (PendingNewsResult pending in _pendingResults.Reader.ReadAllAsync(stoppingToken))
        {
            Interlocked.Exchange(ref _activeSend, 1);
            try
            {
                await SendResultAsync(pending, stoppingToken);
            }
            finally
            {
                Interlocked.Exchange(ref _activeSend, 0);
            }
        }
    }

    private async Task SendResultAsync(PendingNewsResult pending, CancellationToken stoppingToken)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(pending.Result, NewsAgentJsonContext.Default.NewsAgentResult);
        ResultMessage message = new()
        {
            DeliveryId = pending.DeliveryId,
            ResultJson = ByteString.CopyFrom(json)
        };
        int serializedSize = message.CalculateSize();
        if (serializedSize > _options.MaxSendMessageSize)
        {
            LogResultLost(_logger, "oversized_result");
            return;
        }

        for (int attempt = 1; attempt <= _options.MaxSendAttempts; attempt++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                LogResultLost(_logger, "shutdown_before_attempt");
                return;
            }
            using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            request.CancelAfter(_options.RequestTimeout);
            try
            {
                using AsyncUnaryCall<ResultAcknowledgement> call = _client.SubmitResultAsync(message,
                    cancellationToken: request.Token);
                ResultAcknowledgement acknowledgement = await call.ResponseAsync;
                if (!string.Equals(acknowledgement.DeliveryId, pending.DeliveryId, StringComparison.Ordinal))
                {
                    LogDeliveryUnknown(_logger, "invalid_ack", attempt);
                }
                else if (acknowledgement.Accepted)
                {
                    LogDeliveryAcknowledged(_logger);
                    return;
                }
                else
                {
                    LogDeliveryRejected(_logger);
                    return;
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                LogDeliveryUnknown(_logger, "timeout", attempt);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                LogDeliveryUnknown(_logger, "shutdown", attempt);
                return;
            }
            catch (RpcException) when (stoppingToken.IsCancellationRequested)
            {
                LogDeliveryUnknown(_logger, "shutdown", attempt);
                return;
            }
            catch (RpcException) when (request.IsCancellationRequested)
            {
                LogDeliveryUnknown(_logger, "timeout", attempt);
            }
            catch (RpcException error)
            {
                LogDeliveryUnknown(_logger, GetRpcCategory(error), attempt);
            }

            if (attempt == _options.MaxSendAttempts)
            {
                LogResultLost(_logger, "attempts_exhausted");
                return;
            }
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, attempt * 100)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                LogDeliveryUnknown(_logger, "shutdown_before_retry", attempt);
                return;
            }
        }
    }

    /// <summary>Закрывает приём, даёт каналу дренироваться и прерывает его только по общему сроку хоста.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopAccepting();
        bool timedOut = false;
        bool activeSendAtDeadline = false;
        int queuedAtDeadline = 0;
        Task? execution = ExecuteTask;
        try
        {
            if (execution is not null) await execution.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            activeSendAtDeadline = Volatile.Read(ref _activeSend) != 0;
            queuedAtDeadline = _pendingResults.Reader.CanCount ? _pendingResults.Reader.Count : -1;
        }
        finally
        {
            await base.StopAsync(CancellationToken.None);
        }

        if (timedOut)
            LogDrainExpired(_logger, queuedAtDeadline, activeSendAtDeadline);
    }

    private void StopAccepting()
    {
        Interlocked.Exchange(ref _accepting, 0);
        if (Interlocked.Exchange(ref _subscribed, 0) != 0)
            _executor.AnalysisCompleted -= OnAnalysisCompleted;
        _pendingResults.Writer.TryComplete();
    }

    private static string GetRpcCategory(RpcException error) => error.StatusCode switch
    {
        StatusCode.Unavailable => "transport_unavailable",
        StatusCode.DeadlineExceeded => "transport_timeout",
        StatusCode.ResourceExhausted => "transport_rejected_size",
        _ => "transport_failure"
    };

    public override void Dispose()
    {
        StopAccepting();
        base.Dispose();
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "News result accepted by gRPC peer.")]
    private static partial void LogDeliveryAcknowledged(ILogger logger);
    [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "News result rejected by gRPC peer.")]
    private static partial void LogDeliveryRejected(ILogger logger);
    [LoggerMessage(EventId = 22, Level = LogLevel.Warning, Message = "News result delivery outcome unknown: category={Category} attempt={Attempt}.")]
    private static partial void LogDeliveryUnknown(ILogger logger, string category, int attempt);
    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "News result lost: category={Category}.")]
    private static partial void LogResultLost(ILogger logger, string category);
    [LoggerMessage(EventId = 24, Level = LogLevel.Warning, Message = "News result drain expired: queued={Queued} activeSendUnknown={ActiveSendUnknown}.")]
    private static partial void LogDrainExpired(ILogger logger, int queued, bool activeSendUnknown);

    private sealed record PendingNewsResult(string DeliveryId, NewsAgentResult Result);
}
