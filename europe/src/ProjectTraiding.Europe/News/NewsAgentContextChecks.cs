using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTraiding.Europe.Telegram;

namespace ProjectTraiding.Europe.News;

/// <summary>Локальная управляемая проверка замены контекста без сети и без API-вызовов.</summary>
internal static class NewsAgentContextChecks
{
    internal static async Task RunAsync()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ActiveProfile"] = "default",
                ["OpenAI:Profiles:default:Provider"] = "OpenAI",
                ["OpenAI:Profiles:default:Model"] = "gpt-6-luna",
                ["OpenAI:Profiles:gpt41:Provider"] = "OpenAI",
                ["OpenAI:Profiles:gpt41:Model"] = "gpt-4.1-2025-04-14"
            })
            .Build();
        ControlledChatClient chatClient = new();
        EuropeTelegramRuntime telegram = new(new TelegramOptions(), NullLogger<EuropeTelegramRuntime>.Instance);
        NewsAgentExecutor executor = new(telegram, new NewsAgentOptions
        {
            QueueCapacity = 1,
            Workers = 1,
            RequestTimeout = TimeSpan.FromSeconds(30)
        }, NullLogger<NewsAgentExecutor>.Instance, null!, configuration, chatClient);

        try
        {
            NewsAgentPromptSnapshot oldSubject = CreateSnapshot("snapshot-a", "prompt-a", "Synthetic prompt A",
                ["A", "B", "C"]);
            executor.UpdatePromptSnapshot(oldSubject);
            string oldVersion = executor.CurrentExecutionSnapshot!.AgentVersion;

            Task oldRequest = executor.AnalyzeForDiagnosticsAsync(CreatePublication(), CancellationToken.None);
            ControlledChatClient.CapturedCall oldCall = await chatClient.NextCallAsync();
            AssertCall(oldCall, "gpt-6-luna", "Synthetic prompt A", ["A", "B", "C"]);
            if (oldCall.ReasoningEffort != ReasoningEffort.None)
                throw new InvalidOperationException("GPT-6 Luna не получил совместимые reasoning-настройки.");

            NewsAgentPromptSnapshot newSubject = CreateSnapshot("snapshot-b", "prompt-b", "Synthetic prompt B",
                ["A", "C", "D"]);
            executor.UpdatePromptSnapshot(newSubject);
            configuration["OpenAI:ActiveProfile"] = "gpt41";
            configuration.Reload();

            NewsAgentExecutionSnapshot newExecution = executor.CurrentExecutionSnapshot
                ?? throw new InvalidOperationException("После обновления отсутствует рабочая конфигурация News.");
            if (newExecution.AgentVersion == oldVersion || newExecution.Profile.Model != "gpt-4.1-2025-04-14")
                throw new InvalidOperationException("agent_version не отражает активный профиль и снимок.");

            Task newRequest = executor.AnalyzeForDiagnosticsAsync(CreatePublication(), CancellationToken.None);
            ControlledChatClient.CapturedCall newCall = await chatClient.NextCallAsync();
            AssertCall(newCall, "gpt-4.1-2025-04-14", "Synthetic prompt B", ["A", "C", "D"]);
            if (newCall.ReasoningEffort is not null)
                throw new InvalidOperationException("В профиль GPT-4.1 попала настройка reasoning от другой модели.");
            if (chatClient.DisposeCount != 0)
                throw new InvalidOperationException("IChatClient освобождён, пока запросы ещё удерживаются.");

            chatClient.Release(oldCall.Number);
            chatClient.Release(newCall.Number);
            await ExpectControlledFailureAsync(oldRequest);
            await ExpectControlledFailureAsync(newRequest);

            string currentVersion = executor.CurrentExecutionSnapshot!.AgentVersion;
            NewsAgentPromptSnapshot invalid = newSubject with { Prompt = " " };
            try
            {
                executor.UpdatePromptSnapshot(invalid);
                throw new InvalidOperationException("Некорректный снимок был принят.");
            }
            catch (ArgumentException)
            {
            }
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Отклонённый снимок заменил последнюю рабочую конфигурацию.");

            configuration["OpenAI:Profiles:gpt41:Provider"] = "Anthropic";
            configuration.Reload();
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Неподдерживаемый профиль заменил последнюю рабочую конфигурацию.");

            Task retainedRequest = executor.AnalyzeForDiagnosticsAsync(CreatePublication(), CancellationToken.None);
            ControlledChatClient.CapturedCall retainedCall = await chatClient.NextCallAsync();
            AssertCall(retainedCall, "gpt-4.1-2025-04-14", "Synthetic prompt B", ["A", "C", "D"]);
            chatClient.Release(retainedCall.Number);
            await ExpectControlledFailureAsync(retainedRequest);
        }
        finally
        {
            chatClient.ReleaseAll();
            executor.Dispose();
            telegram.Dispose();
        }

        if (chatClient.DisposeCount != 1)
            throw new InvalidOperationException("Управляемый IChatClient должен освобождаться только при закрытии исполнителя.");
    }

    private static NewsAgentPromptSnapshot CreateSnapshot(string version, string promptVersion, string prompt,
        ImmutableArray<string> secids)
    {
        using JsonDocument schema = JsonDocument.Parse(NewsAgentJsonContext.ContractSchema);
        ImmutableArray<NewsInstrumentContextDTO> instruments = secids
            .Select(secid => new NewsInstrumentContextDTO(secid, $"Synthetic {secid}", "equity", null))
            .ToImmutableArray();
        return new NewsAgentPromptSnapshot(version, promptVersion, prompt, "Realtime", ["other"], [], instruments,
            schema.RootElement.Clone(), 256, 0.2f, false);
    }

    private static TelegramChannelPost CreatePublication() => new(1, null, "Synthetic", 1,
        "Local context capture check", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static void AssertCall(ControlledChatClient.CapturedCall call, string expectedModel,
        string expectedPrompt, ImmutableArray<string> expectedSecids)
    {
        if (call.ModelId != expectedModel || call.SystemPrompt != expectedPrompt)
            throw new InvalidOperationException("Вызов не захватил ожидаемые модель и промпт.");
        if (call.Temperature is not { } temperature || Math.Abs(temperature - 0.2f) > 0.0001f)
            throw new InvalidOperationException("Вызов не захватил ожидаемую настройку temperature.");

        using JsonDocument userInput = JsonDocument.Parse(call.UserInput);
        ImmutableArray<string> actualSecids = userInput.RootElement.GetProperty("instruments")
            .EnumerateArray()
            .Select(instrument => instrument.GetProperty("secid").GetString()!)
            .ToImmutableArray();
        if (!actualSecids.SequenceEqual(expectedSecids))
            throw new InvalidOperationException("Вызов не захватил ожидаемый полный набор инструментов.");
    }

    private static async Task ExpectControlledFailureAsync(Task request)
    {
        try
        {
            await request;
        }
        catch (InvalidOperationException error) when (error.Message == ControlledChatClient.ReleasedMessage)
        {
            return;
        }

        throw new InvalidOperationException("Управляемый локальный вызов завершился неожиданно.");
    }

    private sealed class ControlledChatClient : IChatClient
    {
        internal const string ReleasedMessage = "Локальный захват контекста завершён.";
        private readonly Channel<CapturedCall> _calls = Channel.CreateUnbounded<CapturedCall>();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _releases = new();
        private int _callCount;
        private int _disposeCount;

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            int number = Interlocked.Increment(ref _callCount);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releases[number] = release;
            ChatMessage[] capturedMessages = messages.ToArray();
            await _calls.Writer.WriteAsync(new CapturedCall(number, options?.ModelId, options?.Temperature,
                options?.Reasoning?.Effort, capturedMessages[0].Text, capturedMessages[1].Text), cancellationToken);
            await release.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException(ReleasedMessage);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Потоковый вызов не используется локальной диагностикой.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public async Task<CapturedCall> NextCallAsync() => await _calls.Reader.ReadAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        internal void Release(int number)
        {
            if (_releases.TryGetValue(number, out TaskCompletionSource? release)) release.TrySetResult();
        }

        internal void ReleaseAll()
        {
            foreach (TaskCompletionSource release in _releases.Values) release.TrySetResult();
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            _calls.Writer.TryComplete();
        }

        internal sealed record CapturedCall(int Number, string? ModelId, float? Temperature,
            ReasoningEffort? ReasoningEffort, string SystemPrompt, string UserInput);
    }
}
