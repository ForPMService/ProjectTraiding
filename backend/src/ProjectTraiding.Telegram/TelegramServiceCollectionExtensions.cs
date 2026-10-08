using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectTraiding.Telegram;

public static class TelegramServiceCollectionExtensions
{
    public static IServiceCollection AddTelegram(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection("Telegram"));
        services.AddSingleton<TelegramAccountSession>();
        services.AddSingleton<TelegramUpdateHandler>();
        services.AddHostedService<TelegramChannelReceiverService>();
        return services;
    }
}
