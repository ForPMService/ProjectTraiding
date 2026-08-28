using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.CurrentDay;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection;

public static class MoexAlgopackCurrentDayServiceCollectionExtensions
{
    public static IServiceCollection AddMoexAlgopackCurrentDay(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        MoexOptions moexOptions =
            configuration.GetSection("Moex").Get<MoexOptions>() ?? new MoexOptions();
        if (!moexOptions.AlgopackCurrentDayEnabled)
            return services;

        services.TryAddSingleton<MoexReceiverInstrumentReader>();
        services.TryAddSingleton<StreamCursorWriter>();
        services.AddHostedService<AlgopackCurrentDayService>();

        return services;
    }
}
