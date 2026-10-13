using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private readonly IChatClient _client;
    private readonly Channel<TelegramChannelPost> _publications;
    private NewsAgentPromptSnapshot? _promptSnapshot;

    /// <summary>Создаёт только инфраструктуру клиента; конструктор не обращается к API.</summary>
    public NewsAgentExecutor(EuropeTelegramRuntime telegram, NewsAgentOptions options,
        ILogger<NewsAgentExecutor> logger, IHostApplicationLifetime lifetime)
    {
        _telegram = telegram;
        _options = options;
        _logger = logger;
        _lifetime = lifetime;
        _publications = Channel.CreateBounded<TelegramChannelPost>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleReader = options.Workers == 1,
            SingleWriter = false
        });
        // SDK по умолчанию повторяет запросы: здесь физический запрос разрешён только один.
        // OPENAI001 обозначает экспериментальный SDK/адаптер Responses, а не агента или оркестратор.
#pragma warning disable OPENAI001
        ResponsesClientOptions clientOptions = new()
        {
            RetryPolicy = new ClientRetryPolicy(0),
            NetworkTimeout = options.RequestTimeout
        };
        _client = new ResponsesClient(new ApiKeyCredential(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!),
            clientOptions).AsIChatClient();
#pragma warning restore OPENAI001
    }

    /// <summary>Единственный внутренний выход; подписчик должен быть коротким, потокобезопасным и не выполнять I/O.</summary>
    public event Action<NewsAgentResult>? AnalysisCompleted;

    /// <summary>Проверяет полный готовый снимок до атомарной замены; текущие операции сохраняют прежнюю версию.</summary>
    public void UpdatePromptSnapshot(NewsAgentPromptSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidatePromptSnapshot(snapshot);
        // Clone отделяет схему от времени жизни JsonDocument вызывающего кода.
        snapshot = snapshot with { JsonSchema = snapshot.JsonSchema.Clone() };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, NewsAgentJsonContext.Default.NewsAgentPromptSnapshot);
        snapshot = snapshot with { AgentVersion = "news-v3:" + Convert.ToHexString(SHA256.HashData(bytes)) };
        Interlocked.Exchange(ref _promptSnapshot, snapshot);
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
            NewsAgentPromptSnapshot? snapshot = Volatile.Read(ref _promptSnapshot);
            if (snapshot is null)
            {
                LogWarning(_logger, "Нет полного снимка конфигурации; публикация пропущена без вызова LLM.");
                continue;
            }
            using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            request.CancelAfter(_options.RequestTimeout);
            try
            {
                ChatOptions options = new()
                {
                    ModelId = snapshot.Model,
                    MaxOutputTokens = snapshot.MaxOutputTokens,
                    Temperature = snapshot.Temperature,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(snapshot.JsonSchema, "news_v3"),
                    AdditionalProperties = new() { ["strict"] = true },
                    Tools = snapshot.WebSearchEnabled ? [new HostedWebSearchTool()] : null,
                    ToolMode = ChatToolMode.Auto,
                    RawRepresentationFactory = CreateRequestOptions
                };
                ChatMessage[] messages =
                [
                    new(ChatRole.System, snapshot.Prompt),
                    new(ChatRole.User, JsonSerializer.Serialize(new NewsLlmRequest(snapshot.Mode,
                        snapshot.EventTypes, snapshot.GlobalEvents, snapshot.Instruments, publication),
                        NewsAgentJsonContext.Default.NewsLlmRequest))
                ];
                request.Token.ThrowIfCancellationRequested();
                ChatResponse response = await _client.GetResponseAsync(messages, options, request.Token);
#pragma warning disable OPENAI001
                if (response.RawRepresentation is not ResponseResult { Status: ResponseStatus.Completed }
                    || response.ModelId != snapshot.Model)
                    throw new InvalidDataException("Незавершённый ответ LLM.");
#pragma warning restore OPENAI001
                foreach (ChatMessage message in response.Messages)
                    foreach (AIContent content in message.Contents)
                        if (content is ErrorContent) throw new InvalidDataException("Отказ LLM.");
                string json = response.Text;
                ValidateLlmResponse(json);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    AnalysisCompleted?.Invoke(new(publication, json, snapshot.Version, response.ModelId, snapshot.AgentVersion));
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

    /// <summary>Штатная точка расширения адаптера отключает серверное сохранение без дополнительного HTTP-слоя.</summary>
#pragma warning disable OPENAI001
    private static object CreateRequestOptions(IChatClient client) => new CreateResponseOptions { StoredOutputEnabled = false };
#pragma warning restore OPENAI001

    /// <summary>Допускает только полный Realtime-снимок и модель с документированной поддержкой schema и web search.</summary>
    private static void ValidatePromptSnapshot(NewsAgentPromptSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Version) || string.IsNullOrWhiteSpace(snapshot.PromptVersion)
            || string.IsNullOrWhiteSpace(snapshot.Prompt) || snapshot.Mode != "Realtime"
            || snapshot.Model != "gpt-4.1-2025-04-14" || snapshot.MaxOutputTokens is < 1 or > 32768
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
    private static void ValidateLlmResponse(string json)
    {
        NewsLlmResponse analysis = JsonSerializer.Deserialize(json, NewsAgentJsonContext.Default.NewsLlmResponse)
            ?? throw new InvalidDataException("Отсутствует ответ v3.");
        if (analysis.Facts.IsDefault || analysis.Accepted && analysis.Facts.IsEmpty || !analysis.Accepted && !analysis.Facts.IsEmpty)
            throw new InvalidDataException("Несогласованные accepted/facts.");
        // DateTimeOffset допускает строку без зоны; контракт не должен зависеть от локального времени сервера.
        using JsonDocument document = JsonDocument.Parse(json);
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
        base.Dispose();
        _client.Dispose();
    }

    /// <summary>Не включает исходный текст, промпт, JSON, реквизиты или сырые исключения.</summary>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogWarning(ILogger logger, string message);
}
