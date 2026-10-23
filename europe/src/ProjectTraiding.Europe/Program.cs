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
    // Текст вопроса не разбирается как аргумент конфигурации; источники Host остаются стандартными.
    Args = args.Length > 0 && args[0] == "--openai-chat" ? [] : args,
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

    string? apiKey = builder.Configuration["OpenAI:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("Не задан OpenAI:ApiKey в конфигурации.");
        Environment.ExitCode = 2;
        return;
    }

    OpenAiProfileOptions profile = OpenAiProfileOptions.Load(builder.Configuration);
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
