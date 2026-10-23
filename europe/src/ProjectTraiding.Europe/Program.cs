using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Europe;
using ProjectTraiding.Europe.News;
using ProjectTraiding.Europe.Telegram;

if (args.Length == 1 && args[0] == "--agent-aot-probe")
{
    Environment.ExitCode = await AgentAotProbe.RunAsync();
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

    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("Не задан OPENAI_API_KEY.");
        Environment.ExitCode = 2;
        return;
    }

    string model = Environment.GetEnvironmentVariable("OPENAI_MODEL") is { Length: > 0 } selectedModel
        ? selectedModel
        : "gpt-6-luna";
    OpenAIClient client = new(apiKey);
    AIAgent agent = client.GetResponsesClient().AsAIAgent(
        model: model,
        instructions: "Отвечай по-русски. Если данных недостаточно, сообщи об этом.",
        name: "EuropeChatProbe");

    string question = string.Join(" ", args, 1, args.Length - 1);
    AgentResponse response = await agent.RunAsync(question);
    Console.WriteLine(response.Text);
    Environment.ExitCode = string.IsNullOrWhiteSpace(response.Text) ? 1 : 0;
    return;
}

// Собирает минимальный хост и регистрирует один экземпляр приёмника для хоста и его подписчиков.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
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
