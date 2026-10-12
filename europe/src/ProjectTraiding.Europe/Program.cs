using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Europe.Telegram;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
TelegramOptions telegram = TelegramOptions.Load(builder.Configuration);
builder.Services.AddSingleton(telegram);
// Both host and future publication subscribers must use the same runtime instance.
builder.Services.AddSingleton<EuropeTelegramRuntime>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<EuropeTelegramRuntime>());
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = telegram.ShutdownTimeout);

using IHost host = builder.Build();
await host.RunAsync();
