using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using OpenAI.Responses;
using ProjectTraiding.Europe.Telegram;

namespace ProjectTraiding.Europe.News;

/// <summary>Владеет общей ограниченной очередью и фиксированными потребителями; изолирует однократный LLM-вызов.</summary>
public sealed partial class NewsAgentExecutor : BackgroundService
{
    private readonly EuropeTelegramRuntime _telegram;
    private readonly NewsAgentOptions _options;
    private readonly ILogger<NewsAgentExecutor> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, IChatClient> _chatClients;
    private readonly ChatClientAgent _agent;
    private readonly Channel<TelegramChannelPost> _publications;
    private readonly object _configurationGate = new();
    private IDisposable? _configurationReloadRegistration;
    private NewsAgentExecutionSnapshot? _executionSnapshot;

    /// <summary>Создаёт только инфраструктуру клиента; конструктор не обращается к API.</summary>
    public NewsAgentExecutor(EuropeTelegramRuntime telegram, NewsAgentOptions options,
        ILogger<NewsAgentExecutor> logger, IHostApplicationLifetime lifetime, IConfiguration configuration)
        : this(telegram, options, logger, lifetime, configuration, null)
    {
    }

    internal NewsAgentExecutor(EuropeTelegramRuntime telegram, NewsAgentOptions options,
        ILogger<NewsAgentExecutor> logger, IHostApplicationLifetime lifetime, IConfiguration configuration,
        IChatClient? chatClient, IChatClient? xaiChatClient = null)
    {
        _telegram = telegram;
        _options = options;
        _logger = logger;
        _lifetime = lifetime;
        _configuration = configuration;
        LlmProfileOptions activeProfile = LlmProfileOptions.Load(configuration);
        IChatClient activeChatClient = chatClient ?? CreateChatClient(options, activeProfile,
            LlmProfileOptions.GetApiKey(configuration, activeProfile));
        _chatClients = new Dictionary<string, IChatClient>(StringComparer.Ordinal)
        {
            [GetClientCacheKey(activeProfile.Provider, activeProfile.Endpoint)] = activeChatClient
        };
        if (xaiChatClient is not null && activeProfile.Provider != "XAI")
            _chatClients.Add(GetClientCacheKey("XAI", LlmProfileOptions.GetXaiEndpoint(configuration)), xaiChatClient);
        _agent = new ChatClientAgent(activeChatClient, new ChatClientAgentOptions
        {
            UseProvidedChatClientAsIs = true
        });
        _publications = Channel.CreateBounded<TelegramChannelPost>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleReader = options.Workers == 1,
            SingleWriter = false
        });
        _configurationReloadRegistration = ChangeToken.OnChange(configuration.GetReloadToken, OnConfigurationReload);
    }

    private IChatClient GetOrCreateChatClient(LlmProfileOptions profile)
    {
        string apiKey = LlmProfileOptions.GetApiKey(_configuration, profile);
        string key = GetClientCacheKey(profile.Provider, profile.Endpoint);
        if (_chatClients.TryGetValue(key, out IChatClient? chatClient)) return chatClient;

        IChatClient createdClient = CreateChatClient(_options, profile, apiKey);
        _chatClients.Add(key, createdClient);
        return createdClient;
    }

    private static IChatClient CreateChatClient(NewsAgentOptions options, LlmProfileOptions profile, string apiKey)
    {
#pragma warning disable OPENAI001
        ResponsesClientOptions clientOptions = new()
        {
            RetryPolicy = new ClientRetryPolicy(0),
            NetworkTimeout = options.RequestTimeout,
            Endpoint = profile.Endpoint is null ? null : new Uri(profile.Endpoint)
        };
        return new ResponsesClient(new ApiKeyCredential(apiKey), clientOptions).AsIChatClient();
#pragma warning restore OPENAI001
    }

    private static string GetClientCacheKey(string provider, string? endpoint) => $"{provider}:{endpoint}";

    /// <summary>Единственный внутренний выход; подписчик должен быть коротким, потокобезопасным и не выполнять I/O.</summary>
    public event Action<NewsAgentResult>? AnalysisCompleted;

    /// <summary>Проверяет полный готовый снимок до атомарной замены; текущие операции сохраняют прежнюю версию.</summary>
    public void UpdatePromptSnapshot(NewsAgentPromptSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidatePromptSnapshot(snapshot);
        // Clone отделяет схему от времени жизни JsonDocument вызывающего кода.
        snapshot = snapshot with { JsonSchema = snapshot.JsonSchema.Clone() };
        lock (_configurationGate)
        {
            LlmProfileOptions profile = LlmProfileOptions.Load(_configuration);
            Interlocked.Exchange(ref _executionSnapshot, CreateExecutionSnapshot(profile, snapshot));
        }
    }

    internal NewsAgentExecutionSnapshot? CurrentExecutionSnapshot => Volatile.Read(ref _executionSnapshot);

    private NewsAgentExecutionSnapshot CreateExecutionSnapshot(LlmProfileOptions profile,
        NewsAgentPromptSnapshot subject)
    {
        if (profile.Provider == "XAI" && subject.Temperature > 1f)
            throw new InvalidOperationException("Для XAI News Temperature не должна превышать 1.");

        NewsAgentVersionMaterial material = new(profile.Provider, profile.Model, profile.Endpoint,
            profile.ReasoningEffort, subject);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(material,
            NewsAgentJsonContext.Default.NewsAgentVersionMaterial);
        string agentVersion = "news-v3:" + Convert.ToHexString(SHA256.HashData(bytes));
        IChatClient chatClient = GetOrCreateChatClient(profile);
        return new NewsAgentExecutionSnapshot(subject, profile, chatClient, agentVersion);
    }

    private void OnConfigurationReload()
    {
        try
        {
            lock (_configurationGate)
            {
                NewsAgentExecutionSnapshot? current = Volatile.Read(ref _executionSnapshot);
                if (current is null) return;

                LlmProfileOptions profile = LlmProfileOptions.Load(_configuration);
                NewsAgentExecutionSnapshot replacement = CreateExecutionSnapshot(profile, current.Subject);
                if (replacement.AgentVersion != current.AgentVersion)
                    Interlocked.Exchange(ref _executionSnapshot, replacement);
            }
        }
        catch (Exception error)
        {
            LogWarning(_logger, $"Обновление профиля News отклонено: {error.Message}");
        }
    }

    /// <summary>Изолированная проверка использует ту же сборку запроса и валидатор, что и очередь.</summary>
    internal async Task<string> AnalyzeForDiagnosticsAsync(TelegramChannelPost publication,
        CancellationToken cancellationToken)
    {
        NewsAgentExecutionSnapshot snapshot = Volatile.Read(ref _executionSnapshot)
            ?? throw new InvalidOperationException("Нет полного снимка конфигурации News.");
        using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        request.CancelAfter(_options.RequestTimeout);
        ChatResponse response = await GetValidatedResponseAsync(publication, snapshot, request.Token);
        cancellationToken.ThrowIfCancellationRequested();
        return response.Text;
    }

    /// <summary>Подписка выполняется синхронно до запуска следующей hosted-роли Telegram.</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _telegram.PublicationReceived += EnqueuePublication;
        if (AnalysisCompleted is null) LogWarning(_logger, "Потребитель результата LLM не подключён; выдача не означает доставку.");
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Callback не ждёт потребителей и не выполняет сетевые или файловые операции.</summary>
    private void EnqueuePublication(TelegramChannelPost publication)
    {
        if (!_publications.Writer.TryWrite(publication))
            LogWarning(_logger, "Очередь LLM недоступна; публикация пропущена для анализа.");
    }

    /// <summary>Все постоянные потребители читают одну очередь; отмена хоста распространяется на текущие запросы.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, _lifetime.ApplicationStopping);
        using CancellationTokenRegistration stopping = lifetime.Token.Register(Complete);
        Task[] workers = new Task[_options.Workers];
        for (int index = 0; index < workers.Length; index++) workers[index] = ProcessPublicationsAsync(lifetime.Token);
        try { await Task.WhenAll(workers); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            _telegram.PublicationReceived -= EnqueuePublication;
            Complete();
        }
    }

    /// <summary>Снимок читается на старте разбора, а ошибка одной публикации не завершает общий поток.</summary>
    private async Task ProcessPublicationsAsync(CancellationToken cancellationToken)
    {
        await foreach (TelegramChannelPost publication in _publications.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NewsAgentExecutionSnapshot? snapshot = Volatile.Read(ref _executionSnapshot);
            if (snapshot is null)
            {
                LogWarning(_logger, "Нет полного снимка конфигурации; публикация пропущена без вызова LLM.");
                continue;
            }
            using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            request.CancelAfter(_options.RequestTimeout);
            try
            {
                ChatResponse response = await GetValidatedResponseAsync(publication, snapshot, request.Token);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    AnalysisCompleted?.Invoke(new(publication, response.Text, snapshot.Subject.Version,
                        response.ModelId!, snapshot.AgentVersion));
                }
                catch (Exception)
                {
                    LogWarning(_logger, "Подписчик результата LLM завершился ошибкой; доставка не подтверждена.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { LogWarning(_logger, "Истёк срок операции LLM; результат отсутствует."); }
            catch (ClientResultException) { LogWarning(_logger, "Ошибка API LLM; результат отсутствует."); }
            catch (HttpRequestException) { LogWarning(_logger, "Ошибка сети LLM; результат отсутствует."); }
            catch (JsonException) { LogWarning(_logger, "Ответ LLM нарушает JSON-контракт v3."); }
            catch (InvalidDataException) { LogWarning(_logger, "Отказ LLM, незавершённый ответ или нарушение контракта v3."); }
            catch (Exception) { LogWarning(_logger, "Техническая ошибка операции LLM; результат отсутствует."); }
        }
    }

    /// <summary>Возвращает только один завершённый ответ, прошедший проверку отказа и контракта v3.</summary>
    private async Task<ChatResponse> GetValidatedResponseAsync(TelegramChannelPost publication,
        NewsAgentExecutionSnapshot execution, CancellationToken cancellationToken)
    {
        NewsAgentPromptSnapshot snapshot = execution.Subject;
        ChatOptions chatOptions = new()
        {
            ModelId = execution.Profile.Model,
            Instructions = snapshot.Prompt,
            MaxOutputTokens = snapshot.MaxOutputTokens,
            Temperature = snapshot.Temperature,
            Reasoning = execution.Profile.ReasoningEffort is { } effort ? new ReasoningOptions { Effort = effort } : null,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(snapshot.JsonSchema, "news_v3"),
            AdditionalProperties = new() { ["strict"] = true },
            Tools = snapshot.WebSearchEnabled ? [new HostedWebSearchTool()] : null,
            ToolMode = ChatToolMode.Auto,
            RawRepresentationFactory = CreateRequestOptions
        };
        ChatMessage[] messages =
        [
            new(ChatRole.User, JsonSerializer.Serialize(new NewsLlmRequest(snapshot.Mode,
                snapshot.EventTypes, snapshot.GlobalEvents, snapshot.Instruments, publication),
                NewsAgentJsonContext.Default.NewsLlmRequest))
        ];
        ChatClientAgentRunOptions runOptions = new(chatOptions)
        {
            ChatClientFactory = _ => execution.ChatClient
        };
        cancellationToken.ThrowIfCancellationRequested();
        AgentResponse agentResponse = await _agent.RunAsync(messages, options: runOptions,
            cancellationToken: cancellationToken);
        ChatResponse response = agentResponse.AsChatResponse();
#pragma warning disable OPENAI001
        bool completedResponse = execution.Profile.Provider switch
        {
            "OpenAI" => response.RawRepresentation is ResponseResult { Status: ResponseStatus.Completed }
                && response.ModelId == execution.Profile.Model,
            // xAI Responses uses the OpenAI-compatible response envelope with status and model fields.
            "XAI" => response.RawRepresentation is ResponseResult { Status: ResponseStatus.Completed }
                && response.ModelId == execution.Profile.Model,
            _ => false
        };
        if (!completedResponse)
            throw new InvalidDataException("Незавершённый ответ LLM.");
#pragma warning restore OPENAI001
        foreach (ChatMessage message in response.Messages)
            foreach (AIContent content in message.Contents)
                if (content is ErrorContent) throw new InvalidDataException("Отказ LLM.");
        ValidateLlmResponse(response.Text);
        cancellationToken.ThrowIfCancellationRequested();
        return response;
    }

    /// <summary>Штатная точка расширения адаптера отключает серверное сохранение без дополнительного HTTP-слоя.</summary>
#pragma warning disable OPENAI001
    private static object CreateRequestOptions(IChatClient client) => new CreateResponseOptions { StoredOutputEnabled = false };
#pragma warning restore OPENAI001

    /// <summary>Допускает только полный Realtime-снимок и модель с документированной поддержкой schema и web search.</summary>
    private static void ValidatePromptSnapshot(NewsAgentPromptSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Version) || string.IsNullOrWhiteSpace(snapshot.PromptVersion)
            || string.IsNullOrWhiteSpace(snapshot.Prompt) || snapshot.Mode != "Realtime"
            || snapshot.MaxOutputTokens is < 1 or > 32768
            || !float.IsFinite(snapshot.Temperature) || snapshot.Temperature is < 0 or > 2
            || snapshot.EventTypes.IsDefaultOrEmpty || snapshot.GlobalEvents.IsDefault || snapshot.Instruments.IsDefault)
            throw new ArgumentException("Неполный или неподдерживаемый снимок News.");
        foreach (string code in snapshot.EventTypes)
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Некорректный справочник event_type.");
        foreach (NewsGlobalEventDTO item in snapshot.GlobalEvents)
            if (item is null || item.Id <= 0 || string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Некорректный справочник global_event.");
        foreach (NewsInstrumentContextDTO item in snapshot.Instruments)
            if (item is null || string.IsNullOrWhiteSpace(item.Secid) || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(item.InstrumentType) || item.BaseAsset is not null && string.IsNullOrWhiteSpace(item.BaseAsset))
                throw new ArgumentException("Неполный контекст инструмента.");
        using JsonDocument schema = JsonDocument.Parse(NewsAgentJsonContext.ContractSchema);
        if (!JsonElement.DeepEquals(snapshot.JsonSchema, schema.RootElement))
            throw new ArgumentException("JSON Schema не соответствует контракту News v3.");
    }

    /// <summary>Проверяет структуру и технические ограничения v3; существование справочников остаётся Writer.</summary>
    internal static void ValidateLlmResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Корень ответа v3 должен быть объектом.");
        NewsLlmResponse analysis = document.RootElement.Deserialize(NewsAgentJsonContext.Default.NewsLlmResponse)
            ?? throw new InvalidDataException("Отсутствует ответ v3.");
        if (analysis.Facts.IsDefault || analysis.Accepted && analysis.Facts.IsEmpty || !analysis.Accepted && !analysis.Facts.IsEmpty)
            throw new InvalidDataException("Несогласованные accepted/facts.");
        // DateTimeOffset допускает строку без зоны; контракт не должен зависеть от локального времени сервера.
        foreach (JsonElement fact in document.RootElement.GetProperty("facts").EnumerateArray())
        {
            string? time = fact.GetProperty("occurred_at").GetString();
            if (time is not null && !time.EndsWith('Z')
                && !(time.Length >= 6 && time[^6] is '+' or '-' && time[^3] == ':'))
                throw new InvalidDataException("Время факта не содержит часового пояса.");
        }
        foreach (NewsFactDTO fact in analysis.Facts)
        {
            if (fact is null || string.IsNullOrWhiteSpace(fact.EventType) || string.IsNullOrWhiteSpace(fact.Summary)
                || fact.Instruments.IsDefault
                || fact.GlobalEventId is <= 0 || fact.GlobalEventId is not null && fact.NewGlobalEventName is not null
                || fact.NewGlobalEventName is not null && string.IsNullOrWhiteSpace(fact.NewGlobalEventName))
                throw new InvalidDataException("Некорректные поля факта v3.");
            foreach (NewsInstrumentAssessmentDTO instrument in fact.Instruments)
                if (instrument is null || string.IsNullOrWhiteSpace(instrument.Secid)
                    || !double.IsFinite(instrument.InstrumentRelevance) || instrument.InstrumentRelevance is < 0 or > 1
                    || instrument.Direction is not (null or -1 or 0 or 1)
                    || instrument.InstrumentSignificance is double significance
                        && (!double.IsFinite(significance) || significance is < 0 or > 1))
                    throw new InvalidDataException("Некорректная оценка инструмента v3.");
        }
    }

    /// <summary>Закрывает допуск без дренирования: потеря незавершённых публикаций разрешена на этом этапе.</summary>
    private void Complete() => _publications.Writer.TryComplete();

    /// <summary>Снимает подписку и отменяет текущие операции в пределах срока остановки хоста.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _telegram.PublicationReceived -= EnqueuePublication;
        Complete();
        await base.StopAsync(cancellationToken);
        if (ExecuteTask is { IsCompleted: false })
            throw new TimeoutException("LLM не завершил отмену до срока остановки.");
    }

    /// <summary>Освобождает адаптер и подписку; фоновые операции должны быть остановлены хостом.</summary>
    public override void Dispose()
    {
        _telegram.PublicationReceived -= EnqueuePublication;
        Interlocked.Exchange(ref _configurationReloadRegistration, null)?.Dispose();
        base.Dispose();
        lock (_configurationGate)
        {
            HashSet<IChatClient> disposedClients = new(ReferenceEqualityComparer.Instance);
            foreach (IChatClient chatClient in _chatClients.Values)
                if (disposedClients.Add(chatClient)) chatClient.Dispose();
        }
    }

    /// <summary>Не включает исходный текст, промпт, JSON, реквизиты или сырые исключения.</summary>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogWarning(ILogger logger, string message);
}
