using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    public const string ActivitySourceName = "ProjectTraiding.Europe.News";
    public const string MeterName = ActivitySourceName;

    private readonly EuropeTelegramRuntime _telegram;
    private readonly NewsAgentOptions _options;
    private readonly ILogger<NewsAgentExecutor> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IHostEnvironment? _environment;
    private readonly IConfiguration _configuration;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly ObservableGauge<int> _queueDepth;
    private readonly ObservableGauge<int> _queueCapacity;
    private readonly Counter<long> _queueAccepted;
    private readonly Counter<long> _queueRejected;
    private readonly UpDownCounter<long> _processingActive;
    private readonly Counter<long> _processingCompleted;
    private readonly Histogram<double> _processingDuration;
    private readonly Dictionary<string, IChatClient> _chatClients;
    private AIAgent? _agent;
    private readonly Channel<NewsPublicationInput> _publications;
    private readonly object _configurationGate = new();
    private readonly TaskCompletionSource _firstSnapshotReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IDisposable? _configurationReloadRegistration;
    private LlmManagementRuntimeSnapshot _runtimeSnapshot;

    /// <summary>Создаёт только инфраструктуру клиента; конструктор не обращается к API.</summary>
    public NewsAgentExecutor(EuropeTelegramRuntime telegram, NewsAgentOptions options,
        ILogger<NewsAgentExecutor> logger, IHostApplicationLifetime lifetime, IConfiguration configuration,
        IHostEnvironment environment)
        : this(telegram, options, logger, lifetime, configuration, null, null, environment)
    {
    }

    internal NewsAgentExecutor(EuropeTelegramRuntime telegram, NewsAgentOptions options,
        ILogger<NewsAgentExecutor> logger, IHostApplicationLifetime lifetime, IConfiguration configuration,
        IChatClient? chatClient, IChatClient? xaiChatClient = null, IHostEnvironment? environment = null)
    {
        _telegram = telegram;
        _options = options;
        _logger = logger;
        _lifetime = lifetime;
        _environment = environment;
        _configuration = configuration;
        _activitySource = new ActivitySource(ActivitySourceName);
        _meter = new Meter(MeterName);
        ImmutableArray<LlmProfileOptions> profiles = LlmProfileOptions.LoadCatalog(configuration);
        LlmProviderCredentials credentials = LlmProviderCredentials.Load(configuration);
        LlmProfileOptions activeProfile = LlmProfileOptions.LoadConfiguredActiveProfile(configuration, profiles);
        IChatClient? activeChatClient = chatClient;
        string? activeApiKey = credentials.GetApiKey(activeProfile.Provider);
        if (activeChatClient is null && !string.IsNullOrWhiteSpace(activeApiKey))
            activeChatClient = CreateChatClient(options, activeProfile, activeApiKey);
        if (activeChatClient is null && options.Enabled)
            throw new InvalidOperationException($"Отсутствует {activeProfile.Provider}:ApiKey в конфигурации.");

        _chatClients = new Dictionary<string, IChatClient>(StringComparer.Ordinal);
        if (activeChatClient is not null)
            _chatClients.Add(GetClientCacheKey(activeProfile, activeApiKey), activeChatClient);
        if (xaiChatClient is not null && activeProfile.Provider != "XAI")
            _chatClients.Add(GetClientCacheKey("XAI", LlmProfileOptions.GetXaiEndpoint(configuration), null), xaiChatClient);
        if (activeChatClient is not null) _agent = CreateAgent(activeChatClient);
        _runtimeSnapshot = new LlmManagementRuntimeSnapshot(profiles, credentials, activeProfile,
            activeChatClient, false, null);
        _publications = Channel.CreateBounded<NewsPublicationInput>(new BoundedChannelOptions(
            options.Enabled ? options.QueueCapacity : 1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleReader = options.Workers == 1,
            SingleWriter = false
        });
        if (!_publications.Reader.CanCount)
        {
            _meter.Dispose();
            throw new InvalidOperationException("News publication channel must support an exact queue count.");
        }
        _queueDepth = _meter.CreateObservableGauge("projecttraiding.europe.news.queue.depth",
            () => _publications.Reader.Count);
        _queueCapacity = _meter.CreateObservableGauge("projecttraiding.europe.news.queue.capacity",
            () => _options.Enabled ? _options.QueueCapacity : 0);
        _queueAccepted = _meter.CreateCounter<long>("projecttraiding.europe.news.queue.accepted");
        _queueRejected = _meter.CreateCounter<long>("projecttraiding.europe.news.queue.rejected");
        _processingActive = _meter.CreateUpDownCounter<long>("projecttraiding.europe.news.processing.active");
        _processingCompleted = _meter.CreateCounter<long>("projecttraiding.europe.news.processing.completed");
        _processingDuration = _meter.CreateHistogram<double>("projecttraiding.europe.news.processing.duration", "s");
        _configurationReloadRegistration = ChangeToken.OnChange(configuration.GetReloadToken, OnConfigurationReload);
    }

    private IChatClient GetOrCreateChatClient(LlmProfileOptions profile, LlmProviderCredentials credentials)
    {
        string? apiKey = credentials.GetApiKey(profile.Provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Отсутствует {profile.Provider}:ApiKey в конфигурации.");

        string key = GetClientCacheKey(profile, apiKey);
        if (_chatClients.TryGetValue(key, out IChatClient? chatClient)) return chatClient;
        string injectedKey = GetClientCacheKey(profile, null);
        if (_chatClients.TryGetValue(injectedKey, out chatClient)) return chatClient;

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

    private static string GetClientCacheKey(LlmProfileOptions profile, string? apiKey) =>
        GetClientCacheKey(profile.Provider, profile.Endpoint, apiKey);

    private static string GetClientCacheKey(string provider, string? endpoint, string? apiKey)
    {
        string credentialFingerprint = apiKey is null
            ? "injected"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        return $"{provider}:{endpoint}:{credentialFingerprint}";
    }

    private static AIAgent CreateAgent(IChatClient chatClient) => new ChatClientAgent(chatClient,
        new ChatClientAgentOptions { UseProvidedChatClientAsIs = true })
        .AsBuilder()
        .UseOpenTelemetry(null, static telemetry => telemetry.EnableSensitiveData = false)
        .Build(null!);

    /// <summary>Единственный внутренний выход; подписчик должен быть коротким, потокобезопасным и не выполнять I/O.</summary>
    public event Action<NewsAgentResult>? AnalysisCompleted;

    /// <summary>Атомарно применяет полный контекст; уже начатые операции сохраняют захваченный снимок.</summary>
    public void UpdatePromptSnapshot(NewsAgentPromptSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidatePromptSnapshot(snapshot);
        using JsonDocument canonicalSchema = JsonDocument.Parse(NewsAgentJsonContext.ContractSchema);
        // Store the built-in schema so equivalent property order cannot change agent_version.
        snapshot = snapshot with { JsonSchema = canonicalSchema.RootElement.Clone() };
        lock (_configurationGate)
        {
            LlmManagementRuntimeSnapshot current = Volatile.Read(ref _runtimeSnapshot);
            IChatClient chatClient = current.ActiveChatClient
                ?? GetOrCreateChatClient(current.ActiveProfile, current.Credentials);
            NewsAgentExecutionSnapshot execution = CreateExecutionSnapshot(current.ActiveProfile, snapshot, chatClient);
            if (_agent is null) _agent = CreateAgent(chatClient);
            Interlocked.Exchange(ref _runtimeSnapshot, current with
            {
                ActiveChatClient = chatClient,
                Execution = execution
            });
            _firstSnapshotReady.TrySetResult();
        }
    }

    internal NewsSnapshotStatusDTO GetSnapshotStatus()
    {
        LlmManagementRuntimeSnapshot snapshot = Volatile.Read(ref _runtimeSnapshot);
        NewsAgentExecutionSnapshot? execution = snapshot.Execution;
        return new NewsSnapshotStatusDTO(execution is not null,
            _options.Enabled && execution is null, execution?.AgentVersion);
    }

    internal NewsAgentExecutionSnapshot? CurrentExecutionSnapshot => Volatile.Read(ref _runtimeSnapshot).Execution;

    internal void LogStartupState()
    {
        LlmManagementRuntimeSnapshot snapshot = Volatile.Read(ref _runtimeSnapshot);
        LogStartup(_logger, snapshot.ActiveProfile.Provider, snapshot.ActiveProfile.Model,
            snapshot.Execution is not null);
    }

    internal void LogModelSelection(LlmModelSelectionResult result, int httpStatusCode)
    {
        if (result.Failure != LlmModelSelectionFailure.None || result.Profile is null)
        {
            LogModelSelectionRejected(_logger, httpStatusCode, GetSelectionFailureCategory(result.Failure));
            return;
        }

        if (result.Changed && result.PreviousProfile is not null)
        {
            LogModelSelectionChanged(_logger, result.PreviousProfile.Provider, result.PreviousProfile.Model,
                result.Profile.Provider, result.Profile.Model);
            return;
        }

        LogModelSelectionUnchanged(_logger, result.Profile.Provider, result.Profile.Model);
    }

    internal void LogModelSelectionRejected(int httpStatusCode, string reasonCategory) =>
        LogModelSelectionRejected(_logger, httpStatusCode, reasonCategory);

    private static string GetSelectionFailureCategory(LlmModelSelectionFailure failure) => failure switch
    {
        LlmModelSelectionFailure.InvalidProvider or LlmModelSelectionFailure.InvalidModel => "invalid_request",
        LlmModelSelectionFailure.UnknownProvider => "provider_unconfigured",
        LlmModelSelectionFailure.UnknownModel => "model_missing",
        LlmModelSelectionFailure.AmbiguousModel => "ambiguous_profile",
        LlmModelSelectionFailure.MissingApiKey => "provider_key_missing",
        LlmModelSelectionFailure.Unavailable => "client_unavailable",
        _ => "selection_not_applied"
    };

    internal LlmProfileCatalogView GetProfileCatalogView()
    {
        LlmManagementRuntimeSnapshot snapshot = Volatile.Read(ref _runtimeSnapshot);
        return new LlmProfileCatalogView(snapshot.Profiles, snapshot.ActiveProfile);
    }

    internal LlmModelSelectionResult ActivateModel(string? provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return new LlmModelSelectionResult(LlmModelSelectionFailure.InvalidProvider, null);
        string? canonicalProvider = LlmProfileOptions.NormalizeProvider(provider);
        if (canonicalProvider is null)
            return new LlmModelSelectionResult(LlmModelSelectionFailure.UnknownProvider, null);
        if (string.IsNullOrWhiteSpace(model))
            return new LlmModelSelectionResult(LlmModelSelectionFailure.InvalidModel, null);

        lock (_configurationGate)
        {
            LlmManagementRuntimeSnapshot current = Volatile.Read(ref _runtimeSnapshot);
            int matches = 0;
            LlmProfileOptions? selected = null;
            foreach (LlmProfileOptions profile in current.Profiles)
            {
                if (profile.Provider != canonicalProvider || profile.Model != model) continue;
                selected = profile;
                matches++;
            }
            if (matches == 0)
                return new LlmModelSelectionResult(
                    current.Profiles.Any(profile => profile.Provider == canonicalProvider)
                        ? LlmModelSelectionFailure.UnknownModel
                        : LlmModelSelectionFailure.UnknownProvider, null);
            if (matches > 1)
                return new LlmModelSelectionResult(LlmModelSelectionFailure.AmbiguousModel, null);
            if (string.IsNullOrWhiteSpace(current.Credentials.GetApiKey(selected!.Provider)))
                return new LlmModelSelectionResult(LlmModelSelectionFailure.MissingApiKey, null);

            try
            {
                IChatClient chatClient = ProfileEquals(current.ActiveProfile, selected!)
                    ? current.ActiveChatClient ?? GetOrCreateChatClient(selected!, current.Credentials)
                    : GetOrCreateChatClient(selected!, current.Credentials);
                NewsAgentExecutionSnapshot? execution = current.Execution;
                if (execution is not null
                    && (!ProfileEquals(execution.Profile, selected!) || !ReferenceEquals(execution.ChatClient, chatClient)))
                    execution = CreateExecutionSnapshot(selected!, execution.Subject, chatClient);

                if (_agent is null) _agent = CreateAgent(chatClient);
                if (!current.HttpSelectionOverridesConfiguration || !ProfileEquals(current.ActiveProfile, selected!)
                    || !ReferenceEquals(current.ActiveChatClient, chatClient) || current.Execution != execution)
                {
                    LlmManagementRuntimeSnapshot replacement = current with
                    {
                        ActiveProfile = selected!,
                        ActiveChatClient = chatClient,
                        HttpSelectionOverridesConfiguration = true,
                        Execution = execution
                    };
                    Interlocked.Exchange(ref _runtimeSnapshot, replacement);
                }
                LlmManagementRuntimeSnapshot applied = Volatile.Read(ref _runtimeSnapshot);
                bool changed = current.ActiveProfile.Provider != applied.ActiveProfile.Provider
                    || current.ActiveProfile.Model != applied.ActiveProfile.Model;
                return new LlmModelSelectionResult(LlmModelSelectionFailure.None, applied.ActiveProfile,
                    current.ActiveProfile, changed);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or FormatException)
            {
                return new LlmModelSelectionResult(LlmModelSelectionFailure.Unavailable, null);
            }
        }
    }

    private NewsAgentExecutionSnapshot CreateExecutionSnapshot(LlmProfileOptions profile,
        NewsAgentPromptSnapshot subject, IChatClient chatClient)
    {
        if (profile.Provider == "XAI" && subject.Temperature > 1f)
            throw new InvalidOperationException("Для XAI News Temperature не должна превышать 1.");

        NewsAgentVersionMaterial material = new(profile.Provider, profile.Model, profile.Endpoint,
            profile.ReasoningEffort, subject);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(material,
            NewsAgentJsonContext.Default.NewsAgentVersionMaterial);
        string agentVersion = "news-v3:" + Convert.ToHexString(SHA256.HashData(bytes));
        return new NewsAgentExecutionSnapshot(subject, profile, chatClient, agentVersion);
    }

    private void OnConfigurationReload()
    {
        LlmProfileOptions? previousAppliedProfile = null;
        LlmProfileOptions? newAppliedProfile = null;
        bool subjectSnapshotAvailable = false;
        try
        {
            lock (_configurationGate)
            {
                LlmManagementRuntimeSnapshot current = Volatile.Read(ref _runtimeSnapshot);
                ImmutableArray<LlmProfileOptions> profiles = LlmProfileOptions.LoadCatalog(_configuration);
                LlmProviderCredentials credentials = LlmProviderCredentials.Load(_configuration);
                LlmProfileOptions activeProfile = current.HttpSelectionOverridesConfiguration
                    ? ResolvePreservedProfile(current.ActiveProfile, profiles)
                    : LlmProfileOptions.LoadConfiguredActiveProfile(_configuration, profiles);

                bool clientRequired = _options.Enabled || current.HttpSelectionOverridesConfiguration;
                string? activeApiKey = credentials.GetApiKey(activeProfile.Provider);
                if (clientRequired && string.IsNullOrWhiteSpace(activeApiKey))
                    throw new InvalidOperationException("The active provider key is missing.");
                IChatClient? activeChatClient = string.IsNullOrWhiteSpace(activeApiKey)
                    ? null
                    : GetOrCreateChatClient(activeProfile, credentials);

                NewsAgentExecutionSnapshot? execution = current.Execution;
                if (execution is not null)
                {
                    if (activeChatClient is null)
                        throw new InvalidOperationException("The active provider client is unavailable.");
                    if (!ProfileEquals(execution.Profile, activeProfile)
                        || !ReferenceEquals(execution.ChatClient, activeChatClient))
                        execution = CreateExecutionSnapshot(activeProfile, execution.Subject, activeChatClient);
                }

                if (_agent is null && activeChatClient is not null) _agent = CreateAgent(activeChatClient);
                bool changed = !current.Profiles.SequenceEqual(profiles)
                    || current.Credentials != credentials
                    || !ProfileEquals(current.ActiveProfile, activeProfile)
                    || !ReferenceEquals(current.ActiveChatClient, activeChatClient)
                    || !ReferenceEquals(current.Execution, execution);
                if (changed)
                {
                    Interlocked.Exchange(ref _runtimeSnapshot, new LlmManagementRuntimeSnapshot(profiles,
                        credentials, activeProfile, activeChatClient, current.HttpSelectionOverridesConfiguration,
                        execution));
                    if (!ProfileEquals(current.ActiveProfile, activeProfile))
                    {
                        previousAppliedProfile = current.ActiveProfile;
                        newAppliedProfile = activeProfile;
                        subjectSnapshotAvailable = execution is not null;
                    }
                }
            }

            if (previousAppliedProfile is not null && newAppliedProfile is not null)
                LogConfigurationModelApplied(_logger, previousAppliedProfile.Provider, previousAppliedProfile.Model,
                    newAppliedProfile.Provider, newAppliedProfile.Model, subjectSnapshotAvailable);
        }
        catch (Exception)
        {
            LogWarning(_logger, "Обновление LLM-конфигурации отклонено; применённое состояние сохранено.");
        }
    }

    private static LlmProfileOptions ResolvePreservedProfile(LlmProfileOptions activeProfile,
        ImmutableArray<LlmProfileOptions> profiles)
    {
        LlmProfileOptions? match = null;
        foreach (LlmProfileOptions profile in profiles)
        {
            if (profile.Provider != activeProfile.Provider || profile.Model != activeProfile.Model) continue;
            if (match is not null)
                throw new InvalidOperationException("The active model became ambiguous.");
            match = profile;
        }
        return match ?? throw new InvalidOperationException("The active model was removed from configuration.");
    }

    private static bool ProfileEquals(LlmProfileOptions first, LlmProfileOptions second) =>
        first.Provider == second.Provider && first.Name == second.Name && first.Model == second.Model
        && first.Endpoint == second.Endpoint && first.ReasoningEffort == second.ReasoningEffort;

    /// <summary>Изолированная проверка использует ту же сборку запроса и валидатор, что и очередь.</summary>
    internal async Task<string> AnalyzeForDiagnosticsAsync(NewsPublicationInput publication,
        CancellationToken cancellationToken)
    {
        NewsAgentExecutionSnapshot snapshot = Volatile.Read(ref _runtimeSnapshot).Execution
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
        if (_options.Enabled)
        {
            _telegram.PublicationReceived += EnqueueTelegramPublication;
            if (AnalysisCompleted is null)
                LogWarning(_logger, "Потребитель результата LLM не подключён; выдача не означает доставку.");
        }
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Callback не ждёт потребителей и не выполняет сетевые или файловые операции.</summary>
    public bool TryEnqueuePublication(NewsPublicationInput publication)
    {
        if (!_options.Enabled)
        {
            RecordQueueRejected(publication?.Source, "disabled");
            LogWarning(_logger, "Обработка News выключена; публикация пропущена.");
            return false;
        }

        try
        {
            ArgumentNullException.ThrowIfNull(publication);
            if (string.IsNullOrWhiteSpace(publication.Source))
                throw new ArgumentException("Source must contain a source identifier.", nameof(publication));
            if (string.IsNullOrWhiteSpace(publication.RawText))
                throw new ArgumentException("RawText must contain publication text.", nameof(publication));
            JsonElement ownedMetadata;
            if (publication.SourceMetadata.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("SourceMetadata must be a JSON object.", nameof(publication));
            ownedMetadata = publication.SourceMetadata.Clone();
            if (publication.ReceivedAt.Offset != TimeSpan.Zero
                || publication.PublishedAt is DateTimeOffset publishedAt && publishedAt.Offset != TimeSpan.Zero)
                throw new ArgumentException("Publication timestamps must use UTC.", nameof(publication));

            NewsPublicationInput ownedPublication = publication with { SourceMetadata = ownedMetadata };
            if (_publications.Writer.TryWrite(ownedPublication))
            {
                RecordQueueAccepted(publication.Source);
                return true;
            }
        }
        catch (ObjectDisposedException)
        {
            RecordQueueRejected(publication.Source, "invalid");
            throw new ArgumentException("SourceMetadata must be a live JSON object.", nameof(publication));
        }
        catch (ArgumentException)
        {
            RecordQueueRejected(publication?.Source, "invalid");
            throw;
        }
        RecordQueueRejected(publication.Source, "queue_unavailable");
        LogWarning(_logger, "Очередь LLM недоступна; публикация пропущена для анализа.");
        return false;
    }

    private void RecordQueueAccepted(string? source)
    {
        TagList tags = new() { { "source", NormalizeSource(source) } };
        _queueAccepted.Add(1, tags);
    }

    private void RecordQueueRejected(string? source, string reason)
    {
        TagList tags = new() { { "source", NormalizeSource(source) }, { "reason", reason } };
        _queueRejected.Add(1, tags);
    }

    private static string NormalizeSource(string? source) => source switch
    {
        "Telegram" => "Telegram",
        "Bigdata" => "Bigdata",
        _ => "other"
    };

    /// <summary>Преобразует Telegram-вход в независимый общий контракт и изолирует дефект адаптера.</summary>
    internal void EnqueueTelegramPublication(TelegramChannelPost post)
    {
        try
        {
            if (!TryEnqueuePublication(ConvertTelegramPublication(post)))
                return;
        }
        catch (ArgumentException)
        {
            LogWarning(_logger, "Публикация Telegram отклонена: invalid_publication.");
        }
        catch (JsonException)
        {
            LogWarning(_logger, "Публикация Telegram отклонена: metadata_serialization.");
        }
    }

    internal static NewsPublicationInput ConvertTelegramPublication(TelegramChannelPost post)
    {
        ArgumentNullException.ThrowIfNull(post);
        NewsTelegramMetadataDTO metadata = new(post.ChannelId, post.ChannelUsername, post.ChannelTitle,
            post.MessageId);
        JsonElement sourceMetadata = JsonSerializer.SerializeToElement(metadata,
            NewsAgentJsonContext.Default.NewsTelegramMetadataDTO);
        return new NewsPublicationInput("Telegram", post.Text, post.PublishedAt.ToUniversalTime(),
            post.ReceivedAt.ToUniversalTime(), sourceMetadata);
    }

    /// <summary>Все постоянные потребители читают одну очередь; отмена хоста распространяется на текущие запросы.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, _lifetime.ApplicationStopping);
        using CancellationTokenRegistration stopping = lifetime.Token.Register(Complete);
        try
        {
            await _firstSnapshotReady.Task.WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            _telegram.PublicationReceived -= EnqueueTelegramPublication;
            Complete();
            return;
        }
        Task[] workers = new Task[_options.Workers];
        for (int index = 0; index < workers.Length; index++) workers[index] = ProcessPublicationsAsync(lifetime.Token);
        try { await Task.WhenAll(workers); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            _telegram.PublicationReceived -= EnqueueTelegramPublication;
            Complete();
        }
    }

    /// <summary>Снимок читается на старте разбора, а ошибка одной публикации не завершает общий поток.</summary>
    private async Task ProcessPublicationsAsync(CancellationToken cancellationToken)
    {
        await foreach (NewsPublicationInput publication in _publications.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NewsAgentExecutionSnapshot? snapshot = Volatile.Read(ref _runtimeSnapshot).Execution;
            if (snapshot is null)
            {
                LogWarning(_logger, "Нет полного снимка конфигурации; публикация пропущена без вызова LLM.");
                continue;
            }
            using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            request.CancelAfter(_options.RequestTimeout);
            string outcome = "failed";
            TagList sourceTags = new() { { "source", NormalizeSource(publication.Source) } };
            _processingActive.Add(1, sourceTags);
            long startedTimestamp = 0;
            long finishedTimestamp = 0;
            ChatResponse? response = null;
            try
            {
                startedTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    response = await GetValidatedResponseAsync(publication, snapshot, request.Token);
                }
                finally
                {
                    finishedTimestamp = Stopwatch.GetTimestamp();
                }
                outcome = "passed";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                throw;
            }
            catch (OperationCanceledException) { outcome = "timeout"; }
            catch (ClientResultException) { outcome = "failed"; }
            catch (HttpRequestException) { outcome = "failed"; }
            catch (JsonException) { outcome = "failed"; }
            catch (InvalidDataException) { outcome = "failed"; }
            catch (Exception)
            {
                outcome = "failed";
                LogWarning(_logger, "Техническая ошибка операции LLM; результат отсутствует.");
            }
            finally
            {
                _processingActive.Add(-1, sourceTags);
                TagList resultTags = new()
                {
                    { "source", NormalizeSource(publication.Source) },
                    { "outcome", outcome }
                };
                _processingCompleted.Add(1, resultTags);
                _processingDuration.Record(Stopwatch.GetElapsedTime(startedTimestamp, finishedTimestamp).TotalSeconds,
                    resultTags);
            }

            if (outcome != "passed") continue;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                AnalysisCompleted?.Invoke(new(publication, response!.Text,
                    response.ModelId!, snapshot.AgentVersion));
            }
            catch (Exception)
            {
                LogWarning(_logger, "Подписчик результата LLM завершился ошибкой; доставка не подтверждена.");
            }
        }
    }

    /// <summary>Возвращает только один завершённый ответ, прошедший проверку отказа и контракта v3.</summary>
    private async Task<ChatResponse> GetValidatedResponseAsync(NewsPublicationInput publication,
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
        AIAgent agent = Volatile.Read(ref _agent)
            ?? throw new InvalidOperationException("Клиент активной модели не настроен.");

        using Activity? operationActivity = _activitySource.StartActivity("news.llm.request", ActivityKind.Internal);
        Activity? currentActivity = Activity.Current;
        string? traceId = currentActivity?.IdFormat == ActivityIdFormat.W3C
            ? currentActivity.TraceId.ToString()
            : null;
        string operationId = Guid.NewGuid().ToString("N");
        DateTimeOffset sentAtUtc = DateTimeOffset.UtcNow;
        LogLlmRequestStarted(_logger, operationId, traceId, execution.Profile.Provider,
            execution.Profile.Model, execution.AgentVersion, sentAtUtc.ToString("O", CultureInfo.InvariantCulture));
        long startedTimestamp = Stopwatch.GetTimestamp();
        AgentResponse agentResponse;
        ChatResponse response;
        try
        {
            agentResponse = await agent.RunAsync(messages, options: runOptions,
                cancellationToken: cancellationToken);
            response = agentResponse.AsChatResponse();
        }
        catch (Exception error)
        {
            TimeSpan failedDuration = Stopwatch.GetElapsedTime(startedTimestamp);
            string failedDurationText = failedDuration.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture);
            if (error is OperationCanceledException && _lifetime.ApplicationStopping.IsCancellationRequested)
            {
                LogLlmRequestCancelled(_logger, operationId, traceId, execution.Profile.Provider, execution.Profile.Model,
                    "application_stopping", failedDurationText);
            }
            else
            {
                string failureCategory = error switch
                {
                    OperationCanceledException => "timeout_or_cancelled",
                    ClientResultException => "provider_api_error",
                    HttpRequestException => "network_error",
                    _ => "unexpected_error"
                };
                LogLlmRequestFailed(_logger, operationId, traceId, execution.Profile.Provider, execution.Profile.Model,
                    failureCategory, failedDurationText);
            }
            throw;
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedTimestamp);
        DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
        long? inputTokenCount = response.Usage?.InputTokenCount;
        long? outputTokenCount = response.Usage?.OutputTokenCount;
        string? averageTokensPerSecond = outputTokenCount is long outputTokens && elapsed > TimeSpan.Zero
            ? (outputTokens / elapsed.TotalSeconds).ToString("F2", CultureInfo.InvariantCulture)
            : null;
        LogLlmResponseReceived(_logger, operationId, traceId, execution.Profile.Provider,
            execution.Profile.Model, response.ModelId, response.ResponseId, response.FinishReason?.ToString(),
            receivedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture),
            inputTokenCount, outputTokenCount, averageTokensPerSecond);

#pragma warning disable OPENAI001
        ResponseResult? rawResponse = response.RawRepresentation as ResponseResult;
        bool responseStatusCompleted = rawResponse is { Status: ResponseStatus.Completed };
        int? webSearchCalls = null;
        if (rawResponse is not null)
        {
            webSearchCalls = 0;
            foreach (ResponseItem outputItem in rawResponse.OutputItems)
                if (outputItem is WebSearchCallResponseItem) webSearchCalls++;
        }
        long? cachedInputTokenCount = rawResponse?.Usage?.InputTokenDetails?.CachedTokenCount;
        long? reasoningTokenCount = rawResponse?.Usage?.OutputTokenDetails?.ReasoningTokenCount;
#pragma warning restore OPENAI001
        bool reportedModelMatchesRequest = response.ModelId == execution.Profile.Model;
        LogLlmResponseStatus(_logger, operationId, traceId, responseStatusCompleted, reportedModelMatchesRequest);
        if (!responseStatusCompleted || !reportedModelMatchesRequest)
        {
            string failureCategory = !responseStatusCompleted ? "response_not_completed" : "reported_model_mismatch";
            LogLlmValidationResult(_logger, operationId, traceId, "response_status", "rejected", failureCategory);
            throw new InvalidDataException("Незавершённый ответ LLM.");
        }

        foreach (ChatMessage message in response.Messages)
            foreach (AIContent content in message.Contents)
                if (content is ErrorContent)
                {
                    LogLlmValidationResult(_logger, operationId, traceId, "response_content", "rejected", "model_refusal");
                    throw new InvalidDataException("Отказ LLM.");
                }
        try
        {
            ValidateLlmResponse(response.Text);
        }
        catch (JsonException)
        {
            LogLlmValidationResult(_logger, operationId, traceId, "json_v3", "rejected", "invalid_json");
            throw;
        }
        catch (InvalidDataException)
        {
            LogLlmValidationResult(_logger, operationId, traceId, "json_v3", "rejected", "contract_violation");
            throw;
        }
        LogLlmValidationResult(_logger, operationId, traceId, "json_v3", "passed", "none");
        NewsLlmResponse result = JsonSerializer.Deserialize(response.Text, NewsAgentJsonContext.Default.NewsLlmResponse)
            ?? throw new InvalidDataException("Отсутствует ответ v3.");
        LogLlmResponseDiagnostics(_logger, operationId, execution.AgentVersion, execution.Profile.Provider,
            response.ModelId ?? execution.Profile.Model, webSearchCalls, inputTokenCount, outputTokenCount,
            cachedInputTokenCount, reasoningTokenCount, elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture),
            result.Accepted ? "accepted" : "not_accepted");
        cancellationToken.ThrowIfCancellationRequested();
        return response;
    }

    /// <summary>Штатная точка расширения адаптера отключает серверное сохранение без дополнительного HTTP-слоя.</summary>
#pragma warning disable OPENAI001
    private static object CreateRequestOptions(IChatClient client) => new CreateResponseOptions { StoredOutputEnabled = false };
#pragma warning restore OPENAI001

    /// <summary>Допускает только полный Realtime-снимок и модель с документированной поддержкой schema и web search.</summary>
    internal static void ValidatePromptSnapshot(NewsAgentPromptSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Prompt) || snapshot.Mode != "Realtime"
            || snapshot.MaxOutputTokens is < 1 or > 32768
            || !float.IsFinite(snapshot.Temperature) || snapshot.Temperature is < 0 or > 2
            || snapshot.EventTypes.IsDefaultOrEmpty || snapshot.GlobalEvents.IsDefault || snapshot.Instruments.IsDefault)
            throw new ArgumentException("Неполный или неподдерживаемый снимок News.");
        foreach (string code in snapshot.EventTypes)
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Некорректный справочник event_type.");
        foreach (NewsGlobalEventDTO item in snapshot.GlobalEvents)
            if (item is null || item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name))
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
                || fact.GlobalEventId == Guid.Empty || fact.GlobalEventId is not null && fact.NewGlobalEventName is not null
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
        _telegram.PublicationReceived -= EnqueueTelegramPublication;
        Complete();
        await base.StopAsync(cancellationToken);
        if (ExecuteTask is { IsCompleted: false })
            throw new TimeoutException("LLM не завершил отмену до срока остановки.");
    }

    /// <summary>Освобождает адаптер и подписку; фоновые операции должны быть остановлены хостом.</summary>
    public override void Dispose()
    {
        _telegram.PublicationReceived -= EnqueueTelegramPublication;
        Interlocked.Exchange(ref _configurationReloadRegistration, null)?.Dispose();
        base.Dispose();
        _activitySource.Dispose();
        _meter.Dispose();
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

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Europe startup applied LLM selection: provider={Provider} model={Model} subjectSnapshotAvailable={SubjectSnapshotAvailable}.")]
    private static partial void LogStartup(ILogger logger, string provider, string model, bool subjectSnapshotAvailable);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information,
        Message = "LLM model selection applied: {PreviousProvider}/{PreviousModel} -> {Provider}/{Model}; applies to next requests, in-flight requests continue with their captured snapshot.")]
    private static partial void LogModelSelectionChanged(ILogger logger, string previousProvider, string previousModel,
        string provider, string model);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information,
        Message = "LLM model selection accepted without change: provider={Provider} model={Model}.")]
    private static partial void LogModelSelectionUnchanged(ILogger logger, string provider, string model);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "LLM model selection rejected: httpStatusCode={HttpStatusCode} reasonCategory={ReasonCategory}.")]
    private static partial void LogModelSelectionRejected(ILogger logger, int httpStatusCode, string reasonCategory);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information,
        Message = "LLM configuration applied selection: {PreviousProvider}/{PreviousModel} -> {Provider}/{Model}; subjectSnapshotAvailable={SubjectSnapshotAvailable}.")]
    private static partial void LogConfigurationModelApplied(ILogger logger, string previousProvider, string previousModel,
        string provider, string model, bool subjectSnapshotAvailable);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information,
        Message = "LLM request started: operationId={OperationId} traceId={TraceId} provider={Provider} model={RequestedModel} agent_version={AgentVersion} sentAtUtc={SentAtUtc}.")]
    private static partial void LogLlmRequestStarted(ILogger logger, string operationId, string? traceId,
        string provider, string requestedModel, string agentVersion, string sentAtUtc);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information,
        Message = "LLM response received: operationId={OperationId} traceId={TraceId} provider={Provider} requestedModel={RequestedModel} reportedModel={ReportedModel} responseId={ResponseId} finishReason={FinishReason} receivedAtUtc={ReceivedAtUtc} callDurationMs={CallDurationMs} inputTokens={InputTokenCount} outputTokens={OutputTokenCount} averageTokensPerSecondOverFullCall={AverageTokensPerSecond}.")]
    private static partial void LogLlmResponseReceived(ILogger logger, string operationId, string? traceId,
        string provider, string requestedModel, string? reportedModel, string? responseId, string? finishReason,
        string receivedAtUtc, string callDurationMs, long? inputTokenCount, long? outputTokenCount,
        string? averageTokensPerSecond);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "LLM technical completion check: operationId={OperationId} traceId={TraceId} responseStatusCompleted={ResponseStatusCompleted} reportedModelMatchesRequest={ReportedModelMatchesRequest}.")]
    private static partial void LogLlmResponseStatus(ILogger logger, string operationId, string? traceId,
        bool responseStatusCompleted, bool reportedModelMatchesRequest);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information,
        Message = "LLM response validation: operationId={OperationId} traceId={TraceId} stage={Stage} outcome={Outcome} reasonCategory={ReasonCategory}.")]
    private static partial void LogLlmValidationResult(ILogger logger, string operationId, string? traceId, string stage,
        string outcome, string reasonCategory);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
        Message = "LLM request failed: operationId={OperationId} traceId={TraceId} provider={Provider} model={Model} failureCategory={FailureCategory} callDurationMs={CallDurationMs}.")]
    private static partial void LogLlmRequestFailed(ILogger logger, string operationId, string? traceId, string provider,
        string model, string failureCategory, string callDurationMs);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information,
        Message = "LLM request cancelled: operationId={OperationId} traceId={TraceId} provider={Provider} model={Model} cancellationCategory={CancellationCategory} callDurationMs={CallDurationMs}.")]
    private static partial void LogLlmRequestCancelled(ILogger logger, string operationId, string? traceId, string provider,
        string model, string cancellationCategory, string callDurationMs);

    [LoggerMessage(EventId = 15, Level = LogLevel.Warning,
        Message = "News snapshot application failed: reasonCategory={ReasonCategory}.")]
    internal static partial void LogSnapshotApplicationFailed(ILogger logger, string reasonCategory);

    [LoggerMessage(EventId = 16, Level = LogLevel.Information,
        Message = "LLM response diagnostics: operationId={OperationId} agentVersion={AgentVersion} provider={Provider} model={Model} webSearchCalls={WebSearchCalls} inputTokens={InputTokens} outputTokens={OutputTokens} cachedInputTokens={CachedInputTokens} reasoningTokens={ReasoningTokens} callDurationMs={CallDurationMs} result={Result}.")]
    private static partial void LogLlmResponseDiagnostics(ILogger logger, string operationId, string agentVersion,
        string provider, string model, int? webSearchCalls, long? inputTokens, long? outputTokens,
        long? cachedInputTokens, long? reasoningTokens, string callDurationMs, string result);
}
