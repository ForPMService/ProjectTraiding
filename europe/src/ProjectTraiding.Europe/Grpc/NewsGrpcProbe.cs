using System.Net;
using System.Text.Json;
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
        exchange.SetCurrent(ToMessage(first));

        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["News:Enabled"] = "false",
            ["News:QueueCapacity"] = "1",
            ["News:Workers"] = "1",
            ["News:RequestTimeout"] = "00:00:30",
            ["OpenAI:ApiKey"] = "diagnostic-only",
            ["OpenAI:ActiveProfile"] = "default",
            ["OpenAI:Profiles:default:Model"] = "gpt-6-luna",
            ["LLM:ActiveProvider"] = "OpenAI"
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
        NewsSnapshotGrpcSubscriber subscriber = new(grpcOptions, client, executor,
            NullLogger<NewsSnapshotGrpcSubscriber>.Instance);

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
        await WaitUntilAsync(() => executor.GetSnapshotStatus().Revision == 1 && exchange.ConnectionCount >= 1);

        NewsAgentPromptSnapshot revisionTwo = first with { Version = "news-v2", Revision = 2 };
        exchange.Publish(ToMessage(revisionTwo));
        await WaitUntilAsync(() => executor.GetSnapshotStatus().Revision == 2);
        exchange.Publish(ToMessage(revisionTwo));
        exchange.Publish(ToMessage(first));
        exchange.Publish(ToMessage(revisionTwo with { Prompt = "conflicting same revision" }));
        exchange.Publish(new SnapshotMessage { SnapshotJson = Google.Protobuf.ByteString.CopyFromUtf8("{") });
        exchange.Publish(ToMessage(revisionTwo with { Revision = 3, Version = "news-v3" }));
        await WaitUntilAsync(() => executor.GetSnapshotStatus().Revision == 3);
        if (exchange.ConnectionCount != 1)
            throw new InvalidOperationException("Invalid or stale snapshot unexpectedly broke the live stream.");

        exchange.Publish(new SnapshotMessage { SnapshotJson = Google.Protobuf.ByteString.CopyFrom(new byte[8192]) });
        await WaitUntilAsync(() => exchange.ConnectionCount >= 2);
        if (executor.GetSnapshotStatus().Revision != 3)
            throw new InvalidOperationException("Oversized message replaced the active snapshot.");

        exchange.SuppressInitialSnapshot = true;
        exchange.EndStreams();
        await WaitUntilAsync(() => exchange.ConnectionCount >= 3);
        await Task.Delay(grpcOptions.FirstSnapshotTimeout + TimeSpan.FromMilliseconds(100));
        if (executor.GetSnapshotStatus().Revision != 3)
            throw new InvalidOperationException("First-message timeout changed the active snapshot.");
        exchange.SuppressInitialSnapshot = false;
        NewsAgentPromptSnapshot revisionThree = revisionTwo with { Revision = 3, Version = "news-v3" };
        exchange.SetCurrent(ToMessage(revisionThree));
        int connectionBeforeRecovery = exchange.ConnectionCount;
        exchange.EndStreams();
        await WaitUntilAsync(() => exchange.ConnectionCount > connectionBeforeRecovery);
        await WaitUntilAsync(() => executor.GetSnapshotStatus().Revision == 3);

        int connectionsBeforeStop = exchange.ConnectionCount;
        await subscriber.StopAsync(CancellationToken.None);
        await Task.Delay(grpcOptions.ReconnectDelay + TimeSpan.FromMilliseconds(100));
        if (exchange.ConnectionCount != connectionsBeforeStop)
            throw new InvalidOperationException("Subscriber opened a stream after shutdown.");
        await server.StopAsync();
        await server.DisposeAsync();
        executor.Dispose();
        telegram.Dispose();
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
        private SnapshotMessage? _current;
        private int _connectionCount;
        internal int ConnectionCount => Volatile.Read(ref _connectionCount);
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
                _streams.Clear();
            }
        }

        internal void Remove(Channel<SnapshotMessage> stream)
        {
            lock (_gate) _streams.Remove(stream);
            stream.Writer.TryComplete();
        }
    }

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
    }
}
