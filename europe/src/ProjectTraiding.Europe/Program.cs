using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Europe.News;
using ProjectTraiding.Europe.Telegram;

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
