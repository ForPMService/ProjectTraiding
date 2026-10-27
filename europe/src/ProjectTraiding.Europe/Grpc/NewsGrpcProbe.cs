using System.Net;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Europe.News;
using ProjectTraiding.Europe.Telegram;

namespace ProjectTraiding.Europe.Grpc;

/// <summary>Проверяет оба направления протокола на краткоживущем loopback HTTP/2 сервере.</summary>
internal static class NewsGrpcProbe
{
    internal static async Task<int> RunAsync()
    {
        try
        {
            await NewsAgentContextChecks.RunAsync();
            await RunSnapshotChecksAsync();
            Console.WriteLine("Локальная проверка News gRPC завершена: HTTP/2 снимки, применение и reconnect подтверждены.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Проверка News gRPC завершилась ошибкой; результат не выдан.");
            return 1;
        }
    }

    private static async Task RunSnapshotChecksAsync()
    {
        DiagnosticExchange exchange = new();
        WebApplicationBuilder serverBuilder = WebApplication.CreateSlimBuilder();
        serverBuilder.Logging.SetMinimumLevel(LogLevel.None);
        serverBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            listen => listen.Protocols = HttpProtocols.Http2));
        serverBuilder.Services.AddGrpc(options => options.MaxSendMessageSize = 1_048_576);
        serverBuilder.Services.AddSingleton(exchange);
        serverBuilder.Services.AddSingleton<DiagnosticNewsExchange>();
        WebApplication server = serverBuilder.Build();
        server.MapGrpcService<DiagnosticNewsExchange>();
        await server.StartAsync();

        NewsAgentPromptSnapshot first = JsonSerializer.Deserialize(NewsAgentContextChecks.CreateExampleJson(),
            NewsAgentJsonContext.Default.NewsAgentPromptSnapshot)
            ?? throw new InvalidOperationException("Diagnostic snapshot missing.");
        exchange.SetCurrent(ToMessage(first with { Temperature = 1.1f }));

        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["News:Enabled"] = "true",
            ["News:QueueCapacity"] = "1",
            ["News:Workers"] = "1",
            ["News:RequestTimeout"] = "00:00:30",
            ["OpenAI:ApiKey"] = "diagnostic-only",
            ["OpenAI:ActiveProfile"] = "default",
            ["OpenAI:Profiles:default:Model"] = "gpt-6-luna",
            ["LLM:ActiveProvider"] = "XAI",
            ["XAI:ApiKey"] = "diagnostic-only",
            ["XAI:Endpoint"] = "https://api.x.ai/v1",
            ["XAI:ActiveProfile"] = "default",
            ["XAI:Profiles:default:Model"] = "grok-local"
        }).Build();
        NewsAgentOptions newsOptions = NewsAgentOptions.Load(configuration);
        EuropeTelegramRuntime telegram = new(new TelegramOptions(), NullLogger<EuropeTelegramRuntime>.Instance);
        NewsAgentExecutor executor = new(telegram, newsOptions, NullLogger<NewsAgentExecutor>.Instance,
            null!, configuration, null, null, null);
        NewsGrpcOptions grpcOptions = new()
        {
            Enabled = true,
            Address = "http://127.0.0.1:1",
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            FirstSnapshotTimeout = TimeSpan.FromMilliseconds(200),
            MaxReceiveMessageSize = 4096
        };

        Uri address = new(server.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single());
        grpcOptions = grpcOptions with { Address = address.ToString() };
        using GrpcChannel channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = grpcOptions.MaxReceiveMessageSize
        });
        NewsExchange.NewsExchangeClient client = new(channel);
        TestLogger<NewsSnapshotGrpcSubscriber> snapshotLogger = new();
        NewsSnapshotGrpcSubscriber subscriber = new(grpcOptions, client, executor, snapshotLogger);

        NewsGrpcOptions disabledOptions = grpcOptions with { Enabled = false };
        NewsSnapshotGrpcSubscriber disabledSubscriber = new(disabledOptions, client, executor,
            NullLogger<NewsSnapshotGrpcSubscriber>.Instance);
        int beforeDisabled = exchange.ConnectionCount;
        await disabledSubscriber.StartAsync(CancellationToken.None);
        await disabledSubscriber.StopAsync(CancellationToken.None);
        if (exchange.ConnectionCount != beforeDisabled)
            throw new InvalidOperationException("Disabled gRPC subscriber opened a connection.");
        IConfigurationRoot insecureConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["NewsGrpc:Enabled"] = "true", ["NewsGrpc:Address"] = "http://remote.example" }).Build();
        try
        {
            NewsGrpcOptions.Load(insecureConfiguration);
            throw new InvalidOperationException("Remote plaintext gRPC address was accepted.");
        }
        catch (InvalidOperationException error) when (error.Message.StartsWith("NewsGrpc:Address", StringComparison.Ordinal))
        {
        }

        await subscriber.StartAsync(CancellationToken.None);
        if (executor.GetSnapshotStatus() is not { HasSnapshot: false, WaitingForFirstSnapshot: true })
            throw new InvalidOperationException("News worker state did not wait for its first gRPC snapshot.");
        await WaitUntilAsync(() => snapshotLogger.Contains("category=unsupported_provider_configuration"));
        if (executor.GetSnapshotStatus() is not { HasSnapshot: false, WaitingForFirstSnapshot: true })
            throw new InvalidOperationException("Unsupported provider settings changed the active snapshot state.");
        exchange.SetCurrent(ToMessage(first));
        exchange.Publish(ToMessage(first));
        await WaitUntilAsync(() => executor.GetSnapshotStatus().HasSnapshot
            && executor.GetSnapshotStatus().AgentVersion is not null
            && snapshotLogger.Contains("News snapshot applied.") && exchange.ActiveConnectionCount == 1);

        NewsAgentPromptSnapshot snapshotTwo = first with { Prompt = "Snapshot two prompt." };
        int applicationsBeforeSecond = snapshotLogger.CountContaining("News snapshot applied.");
        exchange.Publish(ToMessage(snapshotTwo));
        await WaitUntilAsync(() => snapshotLogger.CountContaining("News snapshot applied.") > applicationsBeforeSecond
            && executor.CurrentExecutionSnapshot!.Subject.Prompt == snapshotTwo.Prompt);

        int applicationsBeforeRepeat = snapshotLogger.CountContaining("News snapshot applied.");
        exchange.Publish(ToMessage(first));
        await WaitUntilAsync(() => snapshotLogger.CountContaining("News snapshot applied.") > applicationsBeforeRepeat
            && executor.CurrentExecutionSnapshot!.Subject.Prompt == first.Prompt);
        if (executor.CurrentExecutionSnapshot!.Subject.Prompt != first.Prompt)
            throw new InvalidOperationException("A valid snapshot without an ordering token was not applied.");

        NewsAgentPromptSnapshot replacement = snapshotTwo with { Prompt = "Replacement prompt." };
        exchange.Publish(ToMessage(replacement));
        await WaitUntilAsync(() => executor.CurrentExecutionSnapshot!.Subject.Prompt == replacement.Prompt);

        exchange.Publish(new SnapshotMessage { SnapshotJson = Google.Protobuf.ByteString.CopyFromUtf8("{") });
        await WaitUntilAsync(() => snapshotLogger.Contains("category=invalid_json"));
        if (executor.CurrentExecutionSnapshot!.Subject.Prompt != replacement.Prompt
            || exchange.ActiveConnectionCount != 1)
            throw new InvalidOperationException("Invalid JSON changed the snapshot or terminated its stream.");

        NewsAgentPromptSnapshot snapshotThree = replacement with { Prompt = "Snapshot three prompt." };
        exchange.SetCurrent(ToMessage(snapshotThree));
        exchange.Publish(ToMessage(snapshotThree));
        await WaitUntilAsync(() => executor.CurrentExecutionSnapshot!.Subject.Prompt == snapshotThree.Prompt);
        if (exchange.ConnectionCount != 1)
            throw new InvalidOperationException("Invalid snapshot unexpectedly broke the live stream.");

        exchange.Publish(new SnapshotMessage { SnapshotJson = Google.Protobuf.ByteString.CopyFrom(new byte[8192]) });
        await WaitUntilAsync(() => exchange.ConnectionCount >= 2);
        if (executor.CurrentExecutionSnapshot!.Subject.Prompt != snapshotThree.Prompt)
            throw new InvalidOperationException("Oversized message replaced the active snapshot.");

        exchange.SuppressInitialSnapshot = true;
        int previousConnections = exchange.ConnectionCount;
        exchange.EndStreams();
        await WaitUntilAsync(() => exchange.ConnectionCount > previousConnections
            && exchange.ActiveConnectionCount == 1);
        int timedOutStreamConnectionCount = exchange.ConnectionCount;
        int previousTimeoutLogs = snapshotLogger.CountContaining("category=initial_snapshot_timeout");
        await WaitUntilAsync(() => snapshotLogger.CountContaining("category=initial_snapshot_timeout") > previousTimeoutLogs);
        await WaitUntilAsync(() => exchange.ActiveConnectionCount == 0);
        if (executor.CurrentExecutionSnapshot!.Subject.Prompt != snapshotThree.Prompt)
            throw new InvalidOperationException("The previous snapshot changed before a replacement was received.");
        await WaitUntilAsync(() => exchange.ConnectionCount > timedOutStreamConnectionCount
            && exchange.ActiveConnectionCount == 1);
        NewsAgentPromptSnapshot snapshotFour = snapshotThree with
        {
            Prompt = "Newly delivered snapshot after the first-message timeout."
        };
        exchange.SetCurrent(ToMessage(snapshotFour));
        exchange.SuppressInitialSnapshot = false;
        exchange.Publish(ToMessage(snapshotFour));
        await WaitUntilAsync(() => executor.CurrentExecutionSnapshot!.Subject.Prompt == snapshotFour.Prompt
            && snapshotLogger.Contains("News snapshot applied."));
        if (executor.CurrentExecutionSnapshot!.Subject.Prompt != snapshotFour.Prompt)
            throw new InvalidOperationException("The post-timeout stream did not apply the new snapshot contents.");

        await RunResultChecksAsync(exchange, client, executor);
        int connectionsBeforeStop = exchange.ConnectionCount;
        Task? subscriberExecution = subscriber.ExecuteTask;
        await subscriber.StopAsync(CancellationToken.None);
        if (subscriberExecution?.IsCompleted != true || exchange.ActiveConnectionCount != 0
            || exchange.ConnectionCount != connectionsBeforeStop)
            throw new InvalidOperationException("Subscriber opened a stream after shutdown.");
        await server.StopAsync();
        await server.DisposeAsync();
        executor.Dispose();
        telegram.Dispose();
    }

    private static async Task RunResultChecksAsync(DiagnosticExchange exchange, NewsExchange.NewsExchangeClient client,
        NewsAgentExecutor executor)
    {
        TestLogger<NewsResultGrpcSender> logger = new();
        NewsGrpcOptions options = new()
        {
            Enabled = true,
            Address = "http://127.0.0.1",
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            FirstSnapshotTimeout = TimeSpan.FromMilliseconds(100),
            RequestTimeout = TimeSpan.FromMilliseconds(500),
            ResultQueueCapacity = 1,
            MaxSendAttempts = 2,
            MaxReceiveMessageSize = 4096,
            MaxSendMessageSize = 4096
        };
        NewsResultGrpcSender sender = new(options, client, executor, logger);
        await sender.StartAsync(CancellationToken.None);

        NewsAgentResult result = CreateResult("synthetic result", "gpt-local", "agent-v");
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Accepted));
        sender.AcceptResult(result);
        ResultMessage accepted = await exchange.ReadSubmissionAsync();
        NewsAgentResult transmitted = JsonSerializer.Deserialize(accepted.ResultJson.Span,
            NewsAgentJsonContext.Default.NewsAgentResult)
            ?? throw new InvalidOperationException("Result JSON was not deserialized.");
        if (transmitted.Publication.Source != result.Publication.Source
            || transmitted.Publication.RawText != result.Publication.RawText
            || transmitted.Publication.PublishedAt != result.Publication.PublishedAt
            || transmitted.Publication.ReceivedAt != result.Publication.ReceivedAt
            || transmitted.Publication.SourceMetadata.GetRawText() != result.Publication.SourceMetadata.GetRawText()
            || transmitted.Json != result.Json
            || transmitted.Model != result.Model || transmitted.AgentVersion != result.AgentVersion
            || accepted.DeliveryId.Length == 0
            || accepted.ResultJson.ToStringUtf8().Contains("configuration_version", StringComparison.OrdinalIgnoreCase)
            || accepted.ResultJson.ToStringUtf8().Contains("revision", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sender changed the result payload or added a revision.");
        await WaitUntilAsync(() => logger.Contains("News result accepted by gRPC peer"));

        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Rejected));
        sender.AcceptResult(result with { Json = "rejected result" });
        _ = await exchange.ReadSubmissionAsync();
        await WaitUntilAsync(() => logger.Contains("News result rejected by gRPC peer"));
        await Task.Delay(150);
        if (exchange.TryReadSubmission(out _))
            throw new InvalidOperationException("accepted=false result was retried.");

        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.InvalidAck));
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.InvalidAck));
        int beforeInvalidAck = exchange.SubmissionCount;
        sender.AcceptResult(result with { Json = "invalid ack result" });
        ResultMessage invalidFirst = await exchange.ReadSubmissionAsync();
        ResultMessage invalidSecond = await exchange.ReadSubmissionAsync();
        AssertSameDelivery(invalidFirst, invalidSecond);
        if (exchange.SubmissionCount != beforeInvalidAck + options.MaxSendAttempts)
            throw new InvalidOperationException("Invalid ACK retry count exceeded MaxSendAttempts.");
        await WaitUntilAsync(() => logger.Contains("attempts_exhausted"));

        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Unavailable));
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Unavailable));
        int beforeUnavailable = exchange.SubmissionCount;
        sender.AcceptResult(result with { Json = "unknown delivery result" });
        ResultMessage unavailableFirst = await exchange.ReadSubmissionAsync();
        NewsAgentPromptSnapshot latestSnapshot = JsonSerializer.Deserialize(NewsAgentContextChecks.CreateExampleJson(),
            NewsAgentJsonContext.Default.NewsAgentPromptSnapshot)!
            with
        { Prompt = "Updated prompt while result delivery retries." };
        exchange.Publish(ToMessage(latestSnapshot));
        await WaitUntilAsync(() => executor.CurrentExecutionSnapshot!.Subject.Prompt == latestSnapshot.Prompt);
        ResultMessage unavailableSecond = await exchange.ReadSubmissionAsync();
        AssertSameDelivery(unavailableFirst, unavailableSecond);
        if (exchange.SubmissionCount != beforeUnavailable + options.MaxSendAttempts)
            throw new InvalidOperationException("Unavailable retry count exceeded MaxSendAttempts.");
        await WaitUntilAsync(() => logger.CountContaining("attempts_exhausted") >= 2);
        if (!logger.Contains("category=transport_unavailable"))
            throw new InvalidOperationException("Transport failure was not classified separately from timeout.");

        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Hold));
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Accepted));
        int beforeTimeout = exchange.SubmissionCount;
        sender.AcceptResult(result with { Json = "timeout delivery result" });
        ResultMessage timeoutFirst = await exchange.ReadSubmissionAsync();
        ResultMessage timeoutSecond = await exchange.ReadSubmissionAsync();
        AssertSameDelivery(timeoutFirst, timeoutSecond);
        if (exchange.SubmissionCount != beforeTimeout + options.MaxSendAttempts)
            throw new InvalidOperationException("Timeout retry count exceeded MaxSendAttempts.");
        await WaitUntilAsync(() => logger.CountContaining("News result accepted by gRPC peer") >= 2);
        if (!logger.Contains("category=timeout"))
            throw new InvalidOperationException("Held SubmitResult did not produce a timeout category.");

        TaskCompletionSource<ResultAcknowledgement> holdFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Hold, holdFirst));
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Accepted));
        sender.AcceptResult(result with { Json = "buffer first" });
        ResultMessage active = await exchange.ReadSubmissionAsync();
        int acknowledgedBeforeHold = logger.CountContaining("News result accepted by gRPC peer");
        DateTimeOffset acceptedCallbackStart = DateTimeOffset.UtcNow;
        sender.AcceptResult(result with { Json = "buffer second" });
        if (DateTimeOffset.UtcNow - acceptedCallbackStart > TimeSpan.FromSeconds(1))
            throw new InvalidOperationException("Result event handler waited on the active network call.");
        DateTimeOffset enqueueStart = DateTimeOffset.UtcNow;
        sender.AcceptResult(result with { Json = "dropped full" });
        if (DateTimeOffset.UtcNow - enqueueStart > TimeSpan.FromSeconds(1) || !logger.Contains("queue_full"))
            throw new InvalidOperationException("Full result queue blocked or did not report a loss.");
        if (logger.CountContaining("News result accepted by gRPC peer") != acknowledgedBeforeHold)
            throw new InvalidOperationException("Sender marked a result accepted before receiving its ACK.");
        holdFirst.TrySetResult(new ResultAcknowledgement { DeliveryId = active.DeliveryId, Accepted = true });
        ResultMessage drained = await exchange.ReadSubmissionAsync();
        if (drained.ResultJson.ToStringUtf8().Contains("buffer second", StringComparison.Ordinal) is false)
            throw new InvalidOperationException("Queued result was not drained in FIFO order.");
        await WaitUntilAsync(() => logger.CountContaining("News result accepted by gRPC peer") >= 3);

        int sendsBeforeOversized = exchange.SubmissionCount;
        sender.AcceptResult(CreateResult(new string('x', 10_000), "gpt-local", "agent-v"));
        await WaitUntilAsync(() => logger.Contains("oversized_result"));
        if (exchange.SubmissionCount != sendsBeforeOversized)
            throw new InvalidOperationException("Oversized result reached the gRPC server.");

        TaskCompletionSource<ResultAcknowledgement> drainHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Hold, drainHold));
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Accepted));
        sender.AcceptResult(result with { Json = "shutdown active" });
        ResultMessage shutdownActive = await exchange.ReadSubmissionAsync();
        sender.AcceptResult(result with { Json = "shutdown queued" });
        using CancellationTokenSource shutdownDeadline = new(TimeSpan.FromSeconds(2));
        Task stop = sender.StopAsync(shutdownDeadline.Token);
        drainHold.TrySetResult(new ResultAcknowledgement { DeliveryId = shutdownActive.DeliveryId, Accepted = true });
        _ = await exchange.ReadSubmissionAsync();
        await stop;
        if (sender.ExecuteTask?.IsCompleted != true)
            throw new InvalidOperationException("Sender cycle did not finish after graceful drain.");
        int submissionCountAfterStop = exchange.SubmissionCount;
        sender.AcceptResult(result with { Json = "after shutdown" });
        if (exchange.SubmissionCount != submissionCountAfterStop || !logger.Contains("sender_stopped"))
            throw new InvalidOperationException("Stopped sender remained subscribed or sent another result.");
        sender.Dispose();

        TestLogger<NewsResultGrpcSender> timeoutLogger = new();
        NewsResultGrpcSender timeoutSender = new(options with { RequestTimeout = TimeSpan.FromSeconds(5) },
            client, executor, timeoutLogger);
        await timeoutSender.StartAsync(CancellationToken.None);
        exchange.EnqueuePlan(new SubmissionPlan(SubmissionOutcome.Hold));
        timeoutSender.AcceptResult(result with { Json = "shutdown timeout active" });
        _ = await exchange.ReadSubmissionAsync();
        timeoutSender.AcceptResult(result with { Json = "shutdown timeout queued" });
        using CancellationTokenSource shortDeadline = new(TimeSpan.FromMilliseconds(150));
        DateTimeOffset stopStarted = DateTimeOffset.UtcNow;
        await timeoutSender.StopAsync(shortDeadline.Token);
        if (DateTimeOffset.UtcNow - stopStarted > TimeSpan.FromSeconds(1)
            || !timeoutLogger.Contains("drain expired")
            || !timeoutLogger.Contains("queued=1")
            || !timeoutLogger.Contains("activeSendUnknown=True"))
            throw new InvalidOperationException("Shutdown deadline did not report active unknown delivery and queued loss.");
        Task timeoutExecution = timeoutSender.ExecuteTask
            ?? throw new InvalidOperationException("Stopped sender did not expose its worker task.");
        try
        {
            await timeoutExecution.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException) when (timeoutExecution.IsCanceled)
        {
        }
        if (!timeoutExecution.IsCompleted)
            throw new InvalidOperationException("Sender worker remained active after cooperative cancellation.");
        await WaitUntilAsync(() => exchange.ActiveSubmissionCount == 0);
        int countAtDeadline = exchange.SubmissionCount;
        timeoutSender.AcceptResult(result with { Json = "after timeout shutdown" });
        await timeoutSender.StopAsync(CancellationToken.None);
        if (exchange.SubmissionCount != countAtDeadline || !timeoutLogger.Contains("sender_stopped"))
            throw new InvalidOperationException("Timeout-stopped sender kept accepting or sending results.");
        if (!ReferenceEquals(timeoutExecution, timeoutSender.ExecuteTask) || !timeoutExecution.IsCompleted)
            throw new InvalidOperationException("Repeated stop restarted the sender.");
        timeoutSender.Dispose();
    }

    private static NewsAgentResult CreateResult(string resultJson, string model, string agentVersion)
    {
        using JsonDocument metadata = JsonDocument.Parse("{\"diagnostic\":true}");
        NewsPublicationInput publication = new("Telegram", "synthetic publication", null,
            DateTimeOffset.UtcNow, metadata.RootElement.Clone());
        return new NewsAgentResult(publication, resultJson, model, agentVersion);
    }

    private static void AssertSameDelivery(ResultMessage first, ResultMessage second)
    {
        if (first.DeliveryId != second.DeliveryId || !first.ResultJson.Equals(second.ResultJson))
            throw new InvalidOperationException("Retry changed the delivery ID or payload.");
    }

    private static SnapshotMessage ToMessage(NewsAgentPromptSnapshot snapshot) => new()
    {
        SnapshotJson = Google.Protobuf.ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(snapshot,
            NewsAgentJsonContext.Default.NewsAgentPromptSnapshot))
    };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Diagnostic gRPC assertion did not become true.");
            await Task.Delay(10);
        }
    }

    private sealed class DiagnosticExchange
    {
        private readonly object _gate = new();
        private readonly List<Channel<SnapshotMessage>> _streams = [];
        private readonly Channel<ResultMessage> _submissions = Channel.CreateUnbounded<ResultMessage>();
        private readonly Channel<SubmissionPlan> _submissionPlans = Channel.CreateUnbounded<SubmissionPlan>();
        private SnapshotMessage? _current;
        private int _connectionCount;
        private int _activeConnectionCount;
        private int _activeSubmissionCount;
        private int _submissionCount;
        internal int ConnectionCount => Volatile.Read(ref _connectionCount);
        internal int ActiveConnectionCount => Volatile.Read(ref _activeConnectionCount);
        internal int ActiveSubmissionCount => Volatile.Read(ref _activeSubmissionCount);
        internal int SubmissionCount => Volatile.Read(ref _submissionCount);
        internal bool SuppressInitialSnapshot { get; set; }

        internal void SetCurrent(SnapshotMessage snapshot)
        {
            lock (_gate) _current = snapshot;
        }

        internal ChannelReader<SnapshotMessage> Subscribe(out Channel<SnapshotMessage> stream)
        {
            stream = Channel.CreateUnbounded<SnapshotMessage>();
            lock (_gate)
            {
                _streams.Add(stream);
                Interlocked.Increment(ref _connectionCount);
                Interlocked.Increment(ref _activeConnectionCount);
                if (!SuppressInitialSnapshot && _current is not null) stream.Writer.TryWrite(_current);
            }
            return stream.Reader;
        }

        internal void Publish(SnapshotMessage message)
        {
            lock (_gate)
            {
                foreach (Channel<SnapshotMessage> stream in _streams) stream.Writer.TryWrite(message);
            }
        }

        internal void EndStreams()
        {
            lock (_gate)
            {
                foreach (Channel<SnapshotMessage> stream in _streams) stream.Writer.TryComplete();
            }
        }

        internal void Remove(Channel<SnapshotMessage> stream)
        {
            lock (_gate)
            {
                if (_streams.Remove(stream)) Interlocked.Decrement(ref _activeConnectionCount);
            }
            stream.Writer.TryComplete();
        }

        internal void EnqueuePlan(SubmissionPlan plan) => _submissionPlans.Writer.TryWrite(plan);

        internal async Task<ResultMessage> ReadSubmissionAsync() => await _submissions.Reader.ReadAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        internal bool TryReadSubmission(out ResultMessage message) => _submissions.Reader.TryRead(out message!);

        internal async Task<ResultAcknowledgement> SubmitAsync(ResultMessage message, ServerCallContext context)
        {
            Interlocked.Increment(ref _submissionCount);
            Interlocked.Increment(ref _activeSubmissionCount);
            try
            {
                await _submissions.Writer.WriteAsync(new ResultMessage(message));
                SubmissionPlan plan = await _submissionPlans.Reader.ReadAsync(context.CancellationToken);
                if (plan.Outcome == SubmissionOutcome.Unavailable)
                    throw new RpcException(new Status(StatusCode.Unavailable, "diagnostic transport failure"));
                if (plan.Outcome == SubmissionOutcome.Hold)
                {
                    if (plan.Acknowledgement is null)
                        await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                    else
                        return await plan.Acknowledgement.Task.WaitAsync(context.CancellationToken);
                }
                return new ResultAcknowledgement
                {
                    DeliveryId = plan.Outcome == SubmissionOutcome.InvalidAck ? string.Empty : message.DeliveryId,
                    Accepted = plan.Outcome == SubmissionOutcome.Accepted
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeSubmissionCount);
            }
        }
    }

    private enum SubmissionOutcome { Accepted, Rejected, InvalidAck, Unavailable, Hold }
    private sealed record SubmissionPlan(SubmissionOutcome Outcome,
        TaskCompletionSource<ResultAcknowledgement>? Acknowledgement = null);

    private sealed class DiagnosticNewsExchange(DiagnosticExchange exchange) : NewsExchange.NewsExchangeBase
    {
        public override async Task WatchSnapshots(WatchSnapshotsRequest request, IServerStreamWriter<SnapshotMessage> responseStream,
            ServerCallContext context)
        {
            ChannelReader<SnapshotMessage> reader = exchange.Subscribe(out Channel<SnapshotMessage> stream);
            try
            {
                await foreach (SnapshotMessage message in reader.ReadAllAsync(context.CancellationToken))
                    await responseStream.WriteAsync(message);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                exchange.Remove(stream);
            }
        }

        public override Task<ResultAcknowledgement> SubmitResult(ResultMessage request, ServerCallContext context) =>
            exchange.SubmitAsync(request, context);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> _messages = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _messages.Enqueue(formatter(state, exception));
        internal bool Contains(string value) => _messages.Any(message => message.Contains(value, StringComparison.Ordinal));
        internal int CountContaining(string value) => _messages.Count(message => message.Contains(value, StringComparison.Ordinal));
    }
}
