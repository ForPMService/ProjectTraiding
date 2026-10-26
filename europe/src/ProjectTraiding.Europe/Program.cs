using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Europe;
using ProjectTraiding.Europe.News;
using ProjectTraiding.Europe.Telegram;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ProjectTraiding.Europe.Grpc;
using Grpc.Net.ClientFactory;

if (args.Length == 1 && args[0] == "--agent-aot-probe")
{
    Environment.ExitCode = await AgentAotProbe.RunAsync();
    return;
}

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    // Аргументы диагностики — это данные, а не CLI-значения конфигурации.
    Args = args.Length > 0 && (args[0] == "--openai-chat" || args[0] == "--news-json-probe"
        || args[0] == "--news-snapshot-example" || args[0] == "--news-grpc-probe") ? [] : args,
    ContentRootPath = AppContext.BaseDirectory
});
ConfigureOpenTelemetry(builder);

if (args.Length == 1 && args[0] == "--news-grpc-probe")
{
    Environment.ExitCode = await NewsGrpcProbe.RunAsync();
    return;
}

if (args.Length > 0 && args[0] == "--openai-chat")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Укажите вопрос после --openai-chat.");
        Environment.ExitCode = 2;
        return;
    }

    LlmProfileOptions profile = LlmProfileOptions.Load(builder.Configuration);
    if (profile.Provider != "OpenAI")
    {
        Console.Error.WriteLine("Для --openai-chat установите LLM:ActiveProvider=OpenAI.");
        Environment.ExitCode = 2;
        return;
    }

    string? apiKey = builder.Configuration["OpenAI:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("Не задан OpenAI:ApiKey в конфигурации.");
        Environment.ExitCode = 2;
        return;
    }

    OpenAIClient client = new(apiKey);

    using IChatClient chatClient =
        client.GetChatClient(profile.Model).AsIChatClient();

    ChatClientAgent agent = new(chatClient, new ChatClientAgentOptions
    {
        Name = "EuropeChatProbe",
        ChatOptions = new ChatOptions
        {
            Instructions = "Отвечай по-русски. Если данных недостаточно, сообщи об этом.",
            ModelId = profile.Model,
            ToolMode = ChatToolMode.None
        },
        UseProvidedChatClientAsIs = true
    });

    string question = string.Join(" ", args, 1, args.Length - 1);
    AgentResponse response = await agent.RunAsync(question);
    Console.WriteLine(response.Text);
    Environment.ExitCode = string.IsNullOrWhiteSpace(response.Text) ? 1 : 0;
    return;
}

if (args.Length > 0 && args[0] == "--news-json-probe")
{
    if (args.Length is not (1 or 3))
    {
        Console.Error.WriteLine("Использование: --news-json-probe [publication.json snapshot.json]");
        Environment.ExitCode = 2;
        return;
    }

    try
    {
        NewsJsonContractChecks.Run();
        if (args.Length == 1)
        {
            using IHost contextCheckHost = builder.Build();
            InitializeOpenTelemetryProviders(contextCheckHost.Services);
            await NewsAgentContextChecks.RunAsync();
        }
        else
        {
            await NewsAgentContextChecks.RunAsync();
        }
        Console.WriteLine("Локальные проверки контекста News и JSON-контракта v3 пройдены.");
        if (args.Length == 3)
            await RunNewsJsonProbeAsync(builder, args[1], args[2]);
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Проверка News JSON завершилась ошибкой; результат не выдан.");
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Length == 1 && args[0] == "--news-snapshot-example")
{
    Console.WriteLine(NewsAgentContextChecks.CreateExampleJson());
    return;
}

ConfigureLoopbackUrls(builder);

// Регистрирует один экземпляр приёмника для хоста и его подписчиков.
TelegramOptions telegram = TelegramOptions.Load(builder.Configuration);
NewsAgentOptions news = NewsAgentOptions.Load(builder.Configuration);
NewsGrpcOptions newsGrpc = NewsGrpcOptions.Load(builder.Configuration);
builder.Services.AddSingleton(telegram);
builder.Services.AddSingleton(news);
builder.Services.AddSingleton(newsGrpc);
if (newsGrpc.Enabled)
{
    builder.Services.AddGrpcClient<NewsExchange.NewsExchangeClient>(options =>
    {
        options.Address = new Uri(newsGrpc.Address);
    }).ConfigureChannel(options =>
    {
        options.MaxReceiveMessageSize = newsGrpc.MaxReceiveMessageSize;
    });
    builder.Services.AddHostedService<NewsSnapshotGrpcSubscriber>();
}
// Хост и будущие подписчики публикаций используют один экземпляр приёмника.
builder.Services.AddSingleton<EuropeTelegramRuntime>();
// Состояние управления LLM доступно даже без промпта и при выключенном контуре News.
builder.Services.AddSingleton<NewsAgentExecutor>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<NewsAgentExecutor>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<EuropeTelegramRuntime>());
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = telegram.ShutdownTimeout);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LlmManagementHttpJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, NewsAgentJsonContext.Default);
});

WebApplication app = builder.Build();
int minimumSwitchIntervalSeconds = LoadMinimumSwitchIntervalSeconds(builder.Configuration);
IConfigurationRoot configurationRoot = builder.Configuration;
using IDisposable intervalReloadRegistration = ChangeToken.OnChange(configurationRoot.GetReloadToken,
    () => ReloadMinimumSwitchInterval(builder.Configuration, app, ref minimumSwitchIntervalSeconds));

object postRateGate = new();
TimeProvider timeProvider = TimeProvider.System;
long lastAdmittedPostTimestamp = 0;
bool hasAdmittedPost = false;
app.Use(async (HttpContext context, RequestDelegate next) =>
{
    bool isSwitchPost = HttpMethods.IsPost(context.Request.Method)
        && string.Equals(context.Request.Path.Value, "/llm/models/active", StringComparison.OrdinalIgnoreCase);
    if (!isSwitchPost)
    {
        await next(context);
        return;
    }

    bool isLimited = false;
    int retryAfterSeconds = 0;
    long timestamp = timeProvider.GetTimestamp();
    lock (postRateGate)
    {
        int intervalSeconds = Volatile.Read(ref minimumSwitchIntervalSeconds);
        TimeSpan remaining = hasAdmittedPost
            ? TimeSpan.FromSeconds(intervalSeconds) - timeProvider.GetElapsedTime(lastAdmittedPostTimestamp, timestamp)
            : TimeSpan.Zero;
        if (remaining > TimeSpan.Zero)
        {
            isLimited = true;
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        }
        else
        {
            lastAdmittedPostTimestamp = timestamp;
            hasAdmittedPost = true;
        }
    }

    if (isLimited)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        context.RequestServices.GetRequiredService<NewsAgentExecutor>()
            .LogModelSelectionRejected(StatusCodes.Status429TooManyRequests, "rate_limited");
        await context.Response.WriteAsJsonAsync(new LlmManagementErrorDTO("Too many model changes. Retry later."),
            LlmManagementHttpJsonContext.Default.LlmManagementErrorDTO, null, context.RequestAborted);
        return;
    }

    await next(context);
});

app.MapGet("/llm/models", static (NewsAgentExecutor executor) =>
{
    LlmProfileCatalogView catalog = executor.GetProfileCatalogView();
    LlmModelDTO[] models = new LlmModelDTO[catalog.Profiles.Length];
    for (int index = 0; index < models.Length; index++)
    {
        LlmProfileOptions profile = catalog.Profiles[index];
        bool isActive = profile.Provider == catalog.ActiveProfile.Provider
            && profile.Name == catalog.ActiveProfile.Name;
        models[index] = new LlmModelDTO(profile.Provider, profile.Model, isActive);
    }
    return TypedResults.Ok(models);
});
app.MapPost("/llm/models/active", HandleActivateModelAsync);

if (app.Environment.IsDevelopment())
{
    app.MapGet("/news/snapshot/status", static (NewsAgentExecutor executor) =>
        TypedResults.Json(executor.GetSnapshotStatus(), NewsAgentJsonContext.Default.NewsSnapshotStatusDTO));
    app.MapPost("/news/snapshot", HandleNewsSnapshotAsync);
}

app.Services.GetRequiredService<NewsAgentExecutor>().LogStartupState();
await app.RunAsync();

static async Task<IResult> HandleNewsSnapshotAsync(HttpContext context, NewsAgentExecutor executor)
{
    if (!context.Request.HasJsonContentType())
        return TypedResults.BadRequest(new NewsSnapshotApplyDTO("InvalidRequest", executor.GetSnapshotStatus().Revision));

    NewsAgentPromptSnapshot? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync(context.Request.Body,
            NewsAgentJsonContext.Default.NewsAgentPromptSnapshot, context.RequestAborted);
    }
    catch (JsonException)
    {
        return TypedResults.BadRequest(new NewsSnapshotApplyDTO("InvalidRequest", executor.GetSnapshotStatus().Revision));
    }

    if (request is null)
        return TypedResults.BadRequest(new NewsSnapshotApplyDTO("InvalidRequest", executor.GetSnapshotStatus().Revision));

    NewsSnapshotApplyStatus result;
    try
    {
        result = executor.UpdatePromptSnapshot(request);
    }
    catch (ArgumentException)
    {
        return TypedResults.BadRequest(new NewsSnapshotApplyDTO("InvalidRequest", executor.GetSnapshotStatus().Revision));
    }
    catch (InvalidOperationException error) when (error.Message.StartsWith("Для XAI News Temperature", StringComparison.Ordinal))
    {
        return TypedResults.BadRequest(new NewsSnapshotApplyDTO("InvalidRequest", executor.GetSnapshotStatus().Revision));
    }
    catch (InvalidOperationException error) when (error.Message.EndsWith(":ApiKey в конфигурации.", StringComparison.Ordinal))
    {
        NewsAgentExecutor.LogSnapshotApplicationFailed(context.RequestServices.GetRequiredService<ILogger<NewsAgentExecutor>>(),
            "active_provider_unavailable");
        return TypedResults.Json(new NewsSnapshotApplyDTO("Unavailable", executor.GetSnapshotStatus().Revision),
            NewsAgentJsonContext.Default.NewsSnapshotApplyDTO, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    NewsSnapshotStatusDTO status = executor.GetSnapshotStatus();
    NewsSnapshotApplyDTO response = new(result.ToString(), status.Revision);
    int httpStatus = result is NewsSnapshotApplyStatus.Conflict or NewsSnapshotApplyStatus.Stale
        ? StatusCodes.Status409Conflict : StatusCodes.Status200OK;
    return TypedResults.Json(response, NewsAgentJsonContext.Default.NewsSnapshotApplyDTO, statusCode: httpStatus);
}

static async Task RunNewsJsonProbeAsync(WebApplicationBuilder builder, string publicationPath, string snapshotPath)
{
    string publicationJson = await File.ReadAllTextAsync(publicationPath);
    TelegramChannelPost publication = JsonSerializer.Deserialize(publicationJson,
        NewsAgentJsonContext.Default.TelegramChannelPost)
        ?? throw new InvalidDataException("Файл публикации не содержит Telegram-публикацию.");
    string snapshotJson = await File.ReadAllTextAsync(snapshotPath);
    NewsAgentPromptSnapshot snapshot = JsonSerializer.Deserialize(snapshotJson,
        NewsAgentJsonContext.Default.NewsAgentPromptSnapshot)
        ?? throw new InvalidDataException("Файл снимка не содержит конфигурацию News.");
    if (snapshot.WebSearchEnabled)
        throw new InvalidDataException("Изолированная проверка должна выполняться без web search.");

    NewsAgentOptions options = new()
    {
        Enabled = true,
        QueueCapacity = 1,
        Workers = 1,
        RequestTimeout = TimeSpan.FromSeconds(30)
    };
    builder.Services.AddSingleton<TelegramOptions>(new TelegramOptions());
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<EuropeTelegramRuntime>();
    builder.Services.AddSingleton<NewsAgentExecutor>();

    using IHost diagnosticHost = builder.Build();
    InitializeOpenTelemetryProviders(diagnosticHost.Services);
    NewsAgentExecutor executor = diagnosticHost.Services.GetRequiredService<NewsAgentExecutor>();
    executor.UpdatePromptSnapshot(snapshot);
    NewsPublicationInput input = NewsAgentExecutor.ConvertTelegramPublication(publication);
    await executor.AnalyzeForDiagnosticsAsync(input, CancellationToken.None);
    Console.WriteLine("Пробный запрос LLM завершён; ответ прошёл проверки.");
}

static async Task<Results<Ok<LlmModelDTO>, BadRequest<LlmManagementErrorDTO>,
    Conflict<LlmManagementErrorDTO>, JsonHttpResult<LlmManagementErrorDTO>>> HandleActivateModelAsync(
    HttpContext context, NewsAgentExecutor executor)
{
    if (!context.Request.HasJsonContentType())
    {
        executor.LogModelSelectionRejected(StatusCodes.Status400BadRequest, "invalid_content_type");
        return TypedResults.BadRequest(new LlmManagementErrorDTO("Content-Type must be application/json."));
    }

    LlmActiveModelRequestDTO? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync(context.Request.Body,
            LlmManagementHttpJsonContext.Default.LlmActiveModelRequestDTO, context.RequestAborted);
    }
    catch (JsonException)
    {
        executor.LogModelSelectionRejected(StatusCodes.Status400BadRequest, "malformed_json");
        return TypedResults.BadRequest(new LlmManagementErrorDTO("The request body is not valid JSON."));
    }

    if (request is null)
    {
        executor.LogModelSelectionRejected(StatusCodes.Status400BadRequest, "missing_request_fields");
        return TypedResults.BadRequest(new LlmManagementErrorDTO("The request body must contain provider and model."));
    }

    LlmModelSelectionResult result = executor.ActivateModel(request.Provider, request.Model);
    int statusCode = result.Failure switch
    {
        LlmModelSelectionFailure.InvalidProvider or LlmModelSelectionFailure.UnknownProvider
            or LlmModelSelectionFailure.InvalidModel or LlmModelSelectionFailure.UnknownModel => StatusCodes.Status400BadRequest,
        LlmModelSelectionFailure.AmbiguousModel => StatusCodes.Status409Conflict,
        LlmModelSelectionFailure.MissingApiKey or LlmModelSelectionFailure.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ when result.Failure == LlmModelSelectionFailure.None && result.Profile is not null => StatusCodes.Status200OK,
        _ => StatusCodes.Status503ServiceUnavailable
    };
    executor.LogModelSelection(result, statusCode);
    return result.Failure switch
    {
        LlmModelSelectionFailure.InvalidProvider => TypedResults.BadRequest(
            new LlmManagementErrorDTO("Provider is required.")),
        LlmModelSelectionFailure.UnknownProvider => TypedResults.BadRequest(
            new LlmManagementErrorDTO("Provider is not configured.")),
        LlmModelSelectionFailure.InvalidModel => TypedResults.BadRequest(
            new LlmManagementErrorDTO("Model is required.")),
        LlmModelSelectionFailure.UnknownModel => TypedResults.BadRequest(
            new LlmManagementErrorDTO("Model is not configured for this provider.")),
        LlmModelSelectionFailure.AmbiguousModel => TypedResults.Conflict(
            new LlmManagementErrorDTO("This model identifier matches multiple profiles.")),
        LlmModelSelectionFailure.MissingApiKey => TypedResults.Json(
            new LlmManagementErrorDTO("The selected provider has no configured API key."),
            LlmManagementHttpJsonContext.Default.LlmManagementErrorDTO,
            statusCode: StatusCodes.Status503ServiceUnavailable),
        LlmModelSelectionFailure.Unavailable => TypedResults.Json(
            new LlmManagementErrorDTO("The selected provider client is unavailable."),
            LlmManagementHttpJsonContext.Default.LlmManagementErrorDTO,
            statusCode: StatusCodes.Status503ServiceUnavailable),
        _ when result.Profile is not null => TypedResults.Ok(
            new LlmModelDTO(result.Profile.Provider, result.Profile.Model, true)),
        _ => TypedResults.Json(new LlmManagementErrorDTO("The model selection could not be applied."),
            LlmManagementHttpJsonContext.Default.LlmManagementErrorDTO,
            statusCode: StatusCodes.Status503ServiceUnavailable)
    };
}

static void ConfigureOpenTelemetry(WebApplicationBuilder builder)
{
    bool enableConsoleExporter = builder.Environment.IsDevelopment()
        && builder.Configuration.GetValue<bool>("Telemetry:ConsoleExporterEnabled");
    // The package marks this stable runtime source identifier as experimental.
#pragma warning disable MAAI001
    string sourceName = OpenTelemetryAgent.DefaultSourceName;
#pragma warning restore MAAI001
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing.AddSource(sourceName);
            tracing.AddSource(NewsAgentExecutor.ActivitySourceName);
            if (enableConsoleExporter) tracing.AddConsoleExporter();
        })
        .WithMetrics(metrics =>
        {
            metrics.AddMeter(sourceName);
            metrics.AddMeter(NewsAgentExecutor.MeterName);
            if (enableConsoleExporter) metrics.AddConsoleExporter();
        });
}

static void InitializeOpenTelemetryProviders(IServiceProvider services)
{
    services.GetRequiredService<TracerProvider>();
    services.GetRequiredService<MeterProvider>();
}

static int LoadMinimumSwitchIntervalSeconds(IConfiguration configuration)
{
    string? configuredValue = configuration["LLM:Management:MinSwitchIntervalSeconds"];
    if (configuredValue is null) return 10;
    if (!int.TryParse(configuredValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
        || seconds <= 0)
        throw new InvalidOperationException("LLM:Management:MinSwitchIntervalSeconds must be a positive integer.");
    return seconds;
}

static void ReloadMinimumSwitchInterval(IConfiguration configuration, WebApplication app, ref int currentSeconds)
{
    try
    {
        Volatile.Write(ref currentSeconds, LoadMinimumSwitchIntervalSeconds(configuration));
    }
    catch (InvalidOperationException)
    {
        app.Logger.LogWarning("Обновление интервала переключения LLM отклонено; действует последнее корректное значение.");
    }
}

static void ConfigureLoopbackUrls(WebApplicationBuilder builder)
{
    string? urls = builder.Configuration["urls"]
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
        ?? builder.Configuration["Urls"];
    if (string.IsNullOrWhiteSpace(urls))
    {
        string? httpPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")
            ?? Environment.GetEnvironmentVariable("DOTNET_HTTP_PORTS");
        if (!string.IsNullOrWhiteSpace(httpPorts))
        {
            string[] ports = httpPorts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            urls = string.Join(';', ports.Select(static port => $"http://127.0.0.1:{port}"));
        }
        else
        {
            urls = "http://127.0.0.1:5000";
        }
    }

    ValidateLoopbackUrls(urls);
    IConfigurationSection endpoints = builder.Configuration.GetSection("Kestrel:Endpoints");
    foreach (IConfigurationSection endpoint in endpoints.GetChildren())
    {
        string? endpointUrl = endpoint["Url"];
        if (string.IsNullOrWhiteSpace(endpointUrl))
            throw new InvalidOperationException("Kestrel endpoints must explicitly bind to loopback.");
        ValidateLoopbackUrls(endpointUrl);
    }
    builder.WebHost.UseUrls(urls);
}

static void ValidateLoopbackUrls(string urls)
{
    string[] addresses = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (addresses.Length == 0)
        throw new InvalidOperationException("At least one loopback HTTP address must be configured.");
    foreach (string address in addresses)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/")
            throw new InvalidOperationException("LLM management HTTP endpoints may listen only on loopback HTTP addresses.");
    }
}
