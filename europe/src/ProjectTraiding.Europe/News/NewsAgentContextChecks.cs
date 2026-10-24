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

/// <summary>Локальная управляемая проверка замены контекста и клиента без сети и без API-вызовов.</summary>
internal static class NewsAgentContextChecks
{
    internal static async Task RunAsync()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["News:Enabled"] = "true",
                ["News:QueueCapacity"] = "1",
                ["News:Workers"] = "1",
                ["News:RequestTimeout"] = "00:00:30",
                ["LLM:ActiveProvider"] = "OpenAI",
                ["OpenAI:ApiKey"] = "local-openai-key",
                ["OpenAI:ActiveProfile"] = "default",
                ["OpenAI:Profiles:default:Model"] = "gpt-6-luna",
                ["OpenAI:Profiles:default:ReasoningEffort"] = "None",
                ["OpenAI:Profiles:gpt41:Model"] = "gpt-4.1-2025-04-14",
                ["XAI:Endpoint"] = "https://api.x.ai/v1",
                ["XAI:ActiveProfile"] = "default",
                ["XAI:Profiles:default:Model"] = "grok-4.7",
                ["XAI:Profiles:default:ReasoningEffort"] = "High",
                ["XAI:Profiles:grok46:Model"] = "grok-4.6",
                ["XAI:Profiles:grok46:ReasoningEffort"] = "High"
            })
            .Build();

        NewsAgentOptions options = NewsAgentOptions.Load(configuration);
        ControlledChatClient openAiClient = new("OpenAI");
        ControlledChatClient xaiClient = new("XAI");
        EuropeTelegramRuntime telegram = new(new TelegramOptions(), NullLogger<EuropeTelegramRuntime>.Instance);
        NewsAgentExecutor executor = new(telegram, options, NullLogger<NewsAgentExecutor>.Instance,
            null!, configuration, openAiClient, xaiClient);

        try
        {
            if (configuration["XAI:ApiKey"] is not null)
                throw new InvalidOperationException("Активный OpenAI не должен требовать ключ XAI.");

            NewsAgentPromptSnapshot oldSubject = CreateSnapshot("snapshot-a", "prompt-a", "Synthetic prompt A",
                ["A", "B", "C"]);
            executor.UpdatePromptSnapshot(oldSubject);
            string oldVersion = executor.CurrentExecutionSnapshot!.AgentVersion;

            Task oldOpenAiRequest = executor.AnalyzeForDiagnosticsAsync(CreatePublication(), CancellationToken.None);
            ControlledChatClient.CapturedCall oldOpenAiCall = await openAiClient.NextCallAsync();
            AssertCall(oldOpenAiCall, "OpenAI", "gpt-6-luna", "Synthetic prompt A", ["A", "B", "C"],
                ReasoningEffort.None);

            NewsAgentPromptSnapshot newSubject = CreateSnapshot("snapshot-b", "prompt-b", "Synthetic prompt B",
                ["A", "C", "D"]);
            executor.UpdatePromptSnapshot(newSubject);
            configuration["OpenAI:ActiveProfile"] = "gpt41";
            configuration.Reload();

            NewsAgentExecutionSnapshot newExecution = executor.CurrentExecutionSnapshot
                ?? throw new InvalidOperationException("После обновления отсутствует рабочая конфигурация News.");
            if (newExecution.AgentVersion == oldVersion || newExecution.Profile.Model != "gpt-4.1-2025-04-14")
                throw new InvalidOperationException("agent_version не отражает активный профиль и снимок.");

            Task newOpenAiRequest = executor.AnalyzeForDiagnosticsAsync(CreatePublication(), CancellationToken.None);
            ControlledChatClient.CapturedCall newOpenAiCall = await openAiClient.NextCallAsync();
            AssertCall(newOpenAiCall, "OpenAI", "gpt-4.1-2025-04-14", "Synthetic prompt B", ["A", "C", "D"], null);

            configuration["OpenAI:ApiKey"] = null;
            configuration["XAI:ApiKey"] = "local-xai-key";
            configuration["LLM:ActiveProvider"] = "XAI";
            configuration["XAI:ActiveProfile"] = "default";
            configuration.Reload();
            NewsAgentOptions xaiOptions = NewsAgentOptions.Load(configuration);
            if (!xaiOptions.Enabled)
                throw new InvalidOperationException("Активный XAI профиль не загрузил настройки News.");

            NewsAgentExecutionSnapshot xaiExecution = executor.CurrentExecutionSnapshot
                ?? throw new InvalidOperationException("После переключения отсутствует рабочая конфигурация News.");
            if (xaiExecution.Profile.Provider != "XAI" || xaiExecution.Profile.Model != "grok-4.7"
                || xaiExecution.AgentVersion == newExecution.AgentVersion)
                throw new InvalidOperationException("Снимок не переключился на независимый профиль XAI.");

            Task oldXaiRequest = executor.AnalyzeForDiagnosticsAsync(CreatePublication(), CancellationToken.None);
            ControlledChatClient.CapturedCall oldXaiCall = await xaiClient.NextCallAsync();
            AssertCall(oldXaiCall, "XAI", "grok-4.7", "Synthetic prompt B", ["A", "C", "D"],
                ReasoningEffort.High);

            NewsAgentPromptSnapshot newestSubject = CreateSnapshot("snapshot-c", "prompt-c", "Synthetic prompt C",
                ["A", "D", "E"]);
            executor.UpdatePromptSnapshot(newestSubject);
            configuration["XAI:ActiveProfile"] = "grok46";
            configuration.Reload();

            NewsAgentExecutionSnapshot newestExecution = executor.CurrentExecutionSnapshot
                ?? throw new InvalidOperationException("После смены модели XAI отсутствует рабочая конфигурация.");
            if (newestExecution.Profile.Provider != "XAI" || newestExecution.Profile.Model != "grok-4.6")
                throw new InvalidOperationException("Смена модели XAI не обновила целый снимок.");

            Task newXaiRequest = executor.AnalyzeForDiagnosticsAsync(CreatePublication(), CancellationToken.None);
            ControlledChatClient.CapturedCall newXaiCall = await xaiClient.NextCallAsync();
            AssertCall(newXaiCall, "XAI", "grok-4.6", "Synthetic prompt C", ["A", "D", "E"],
                ReasoningEffort.High);

            if (openAiClient.DisposeCount != 0 || xaiClient.DisposeCount != 0)
                throw new InvalidOperationException("Клиент освобождён, пока старые запросы ещё выполняются.");

            string currentVersion = newestExecution.AgentVersion;
            NewsAgentPromptSnapshot invalid = newestSubject with { Prompt = " " };
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

            try
            {
                executor.UpdatePromptSnapshot(newestSubject with { Temperature = 1.1f });
                throw new InvalidOperationException("XAI принял недопустимый Temperature.");
            }
            catch (InvalidOperationException error) when (error.Message == "Для XAI News Temperature не должна превышать 1.")
            {
            }
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Недопустимый XAI Temperature заменил последнюю рабочую конфигурацию.");

            configuration["XAI:Profiles:grok46:ReasoningEffort"] = "unsupported";
            configuration.Reload();
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Некорректный ReasoningEffort заменил рабочую конфигурацию.");

            configuration["XAI:Profiles:grok46:ReasoningEffort"] = "High";
            configuration["XAI:ApiKey"] = null;
            configuration.Reload();
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Профиль без активного ключа заменил рабочую конфигурацию.");

            openAiClient.Release(oldOpenAiCall.Number);
            openAiClient.Release(newOpenAiCall.Number);
            xaiClient.Release(oldXaiCall.Number);
            xaiClient.Release(newXaiCall.Number);
            await ExpectControlledFailureAsync(oldOpenAiRequest);
            await ExpectControlledFailureAsync(newOpenAiRequest);
            await ExpectControlledFailureAsync(oldXaiRequest);
            await ExpectControlledFailureAsync(newXaiRequest);
        }
        finally
        {
            openAiClient.ReleaseAll();
            xaiClient.ReleaseAll();
            executor.Dispose();
            telegram.Dispose();
        }

        if (openAiClient.DisposeCount != 1 || xaiClient.DisposeCount != 1)
            throw new InvalidOperationException("Каждый управляемый IChatClient должен освобождаться при закрытии исполнителя.");

        IConfigurationRoot disabledConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["News:Enabled"] = "false" })
            .Build();
        if (NewsAgentOptions.Load(disabledConfiguration).Enabled)
            throw new InvalidOperationException("Выключенный контур News не должен требовать настройки провайдера.");
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

    private static void AssertCall(ControlledChatClient.CapturedCall call, string expectedProvider,
        string expectedModel, string expectedPrompt, ImmutableArray<string> expectedSecids,
        ReasoningEffort? expectedReasoningEffort)
    {
        if (call.Provider != expectedProvider || call.ModelId != expectedModel || call.Instructions != expectedPrompt)
            throw new InvalidOperationException("Вызов смешал провайдера, модель или промпт из разных снимков.");
        if (call.ReasoningEffort != expectedReasoningEffort)
            throw new InvalidOperationException("Вызов получил настройки reasoning другого профиля.");
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

        internal ControlledChatClient(string provider) => this.Provider = provider;

        internal string Provider { get; }
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            int number = Interlocked.Increment(ref _callCount);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releases[number] = release;
            ChatMessage[] capturedMessages = messages.ToArray();
            if (capturedMessages.Length != 1 || capturedMessages[0].Role != ChatRole.User)
                throw new InvalidOperationException("Промпт должен передаваться один раз через Instructions.");
            await _calls.Writer.WriteAsync(new CapturedCall(number, Provider, options?.ModelId, options?.Temperature,
                options?.Reasoning?.Effort, options?.Instructions, capturedMessages[0].Text), cancellationToken);
            await release.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException(ReleasedMessage);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Потоковый вызов не используется локальной диагностикой.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        internal async Task<CapturedCall> NextCallAsync() => await _calls.Reader.ReadAsync()
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

        internal sealed record CapturedCall(int Number, string Provider, string? ModelId, float? Temperature,
            ReasoningEffort? ReasoningEffort, string? Instructions, string UserInput);
    }
}
