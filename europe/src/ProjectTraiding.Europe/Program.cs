using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Europe;
using ProjectTraiding.Europe.News;
using ProjectTraiding.Europe.Telegram;

if (args.Length == 1 && args[0] == "--agent-aot-probe")
{
    Environment.ExitCode = await AgentAotProbe.RunAsync();
    return;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    // Аргументы диагностики — это данные, а не CLI-значения конфигурации.
    Args = args.Length > 0 && (args[0] == "--openai-chat" || args[0] == "--news-json-probe") ? [] : args,
    ContentRootPath = AppContext.BaseDirectory
});

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
        await NewsAgentContextChecks.RunAsync();
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

// Регистрирует один экземпляр приёмника для хоста и его подписчиков.
TelegramOptions telegram = TelegramOptions.Load(builder.Configuration);
NewsAgentOptions news = NewsAgentOptions.Load(builder.Configuration);
builder.Services.AddSingleton(telegram);
builder.Services.AddSingleton(news);
// Хост и будущие подписчики публикаций используют один экземпляр приёмника.
builder.Services.AddSingleton<EuropeTelegramRuntime>();
if (news.Enabled)
{
    // StartAsync подписывает LLM до запуска Telegram; оба контура используют тот же экземпляр приёмника.
    builder.Services.AddSingleton<NewsAgentExecutor>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<NewsAgentExecutor>());
}
builder.Services.AddHostedService(provider => provider.GetRequiredService<EuropeTelegramRuntime>());
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = telegram.ShutdownTimeout);

using IHost host = builder.Build();
await host.RunAsync();

static async Task RunNewsJsonProbeAsync(HostApplicationBuilder builder, string publicationPath, string snapshotPath)
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
    NewsAgentExecutor executor = diagnosticHost.Services.GetRequiredService<NewsAgentExecutor>();
    executor.UpdatePromptSnapshot(snapshot);
    string responseJson = await executor.AnalyzeForDiagnosticsAsync(publication, CancellationToken.None);
    Console.WriteLine(responseJson);
}
