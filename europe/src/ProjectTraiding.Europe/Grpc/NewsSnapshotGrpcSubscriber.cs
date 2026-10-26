using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Europe.News;

namespace ProjectTraiding.Europe.Grpc;

/// <summary>Получает полные снимки News и применяет их существующим механизмом редакций.</summary>
public sealed partial class NewsSnapshotGrpcSubscriber : BackgroundService
{
    private readonly NewsGrpcOptions _options;
    private readonly NewsExchange.NewsExchangeClient _client;
    private readonly NewsAgentExecutor _executor;
    private readonly ILogger<NewsSnapshotGrpcSubscriber> _logger;

    /// <summary>Создаёт последовательный gRPC-подписчик.</summary>
    public NewsSnapshotGrpcSubscriber(NewsGrpcOptions options, NewsExchange.NewsExchangeClient client,
        NewsAgentExecutor executor, ILogger<NewsSnapshotGrpcSubscriber> logger)
    {
        _options = options;
        _client = client;
        _executor = executor;
        _logger = logger;
    }

    /// <summary>Восстанавливает завершившиеся или оборванные потоки до остановки хоста.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReceiveSnapshotsAsync(stoppingToken);
                if (!stoppingToken.IsCancellationRequested) LogStreamEnded(_logger);
            }
            catch (RpcException error)
            {
                LogTransportFailure(_logger, GetRpcCategory(error));
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                LogTransportFailure(_logger, "initial_snapshot_timeout");
            }

            if (stoppingToken.IsCancellationRequested) break;
            await Task.Delay(_options.ReconnectDelay, stoppingToken);
        }
    }

    /// <summary>Читает один RPC-поток; срок ограничен только для первого сообщения.</summary>
    internal async Task ReceiveSnapshotsAsync(CancellationToken cancellationToken)
    {
        using AsyncServerStreamingCall<SnapshotMessage> call = _client.WatchSnapshots(new WatchSnapshotsRequest(),
            cancellationToken: cancellationToken);
        using CancellationTokenSource firstMessageTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstMessageTimeout.CancelAfter(_options.FirstSnapshotTimeout);
        bool receivedAny = false;
        while (true)
        {
            bool hasMessage;
            try
            {
                hasMessage = await call.ResponseStream.MoveNext(receivedAny ? cancellationToken : firstMessageTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !receivedAny)
            {
                throw;
            }
            if (!hasMessage) return;
            receivedAny = true;
            ApplySnapshot(call.ResponseStream.Current);
        }
    }

    /// <summary>Десериализует полное предметное сообщение и передаёт его штатному исполнителю.</summary>
    internal void ApplySnapshot(SnapshotMessage message)
    {
        if (message.SnapshotJson.IsEmpty)
        {
            LogSnapshotRejected(_logger, "empty_payload");
            return;
        }
        NewsAgentPromptSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize(message.SnapshotJson.Span,
                NewsAgentJsonContext.Default.NewsAgentPromptSnapshot);
        }
        catch (JsonException)
        {
            LogSnapshotRejected(_logger, "invalid_json");
            return;
        }
        if (snapshot is null)
        {
            LogSnapshotRejected(_logger, "invalid_data");
            return;
        }
        try
        {
            NewsSnapshotApplyStatus status = _executor.UpdatePromptSnapshot(snapshot);
            LogSnapshotApplied(_logger, status.ToString(), snapshot.Revision ?? 0);
        }
        catch (ArgumentException)
        {
            LogSnapshotRejected(_logger, "invalid_snapshot");
        }
        catch (InvalidOperationException)
        {
            LogSnapshotRejected(_logger, "snapshot_unavailable");
        }
    }

    private static string GetRpcCategory(RpcException error) => error.StatusCode switch
    {
        StatusCode.ResourceExhausted => "oversized_snapshot",
        StatusCode.Unavailable => "transport_unavailable",
        StatusCode.DeadlineExceeded => "transport_timeout",
        _ => "transport_failure"
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "News snapshot stream ended; reconnect scheduled.")]
    private static partial void LogStreamEnded(ILogger logger);
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "News snapshot transport ended: category={Category}.")]
    private static partial void LogTransportFailure(ILogger logger, string category);
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "News snapshot rejected: category={Category}.")]
    private static partial void LogSnapshotRejected(ILogger logger, string category);
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "News snapshot handled: status={Status} revision={Revision}.")]
    private static partial void LogSnapshotApplied(ILogger logger, string status, long revision);
}
