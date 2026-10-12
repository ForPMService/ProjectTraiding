using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Europe.Telegram;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
TelegramOptions telegram = TelegramOptions.Load(builder.Configuration);
EuropeTelegramBotOptions bot = EuropeTelegramBotOptions.Load(builder.Configuration);
builder.Services.AddSingleton(telegram);
builder.Services.AddSingleton(bot);
builder.Services.AddMetrics();
builder.Services.AddSingleton<TelegramReceiverController>();
builder.Services.AddHostedService<EuropeTelegramRuntime>();
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = telegram.DrainTimeout + telegram.CloseTimeout + bot.RequestTimeout);

using IHost host = builder.Build();
await host.RunAsync();
