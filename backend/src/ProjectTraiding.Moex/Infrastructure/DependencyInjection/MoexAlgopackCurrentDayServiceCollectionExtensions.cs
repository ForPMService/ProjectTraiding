using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.CurrentDay;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
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
        int batchSize = configuration.GetValue<int>("ClickHouse:CandlesBatchSize", 10000);
        services.AddHostedService(sp => new AlgopackCurrentDayService(
            sp.GetRequiredService<MoexReceiverInstrumentReader>(),
            sp.GetRequiredService<MoexHistoryPageReader>(),
            sp.GetRequiredService<ClickHouseInsertExecutor>(),
            sp.GetRequiredService<StreamCursorWriter>(),
            sp.GetRequiredService<FutoiSubjectReader>(),
            batchSize,
            sp.GetRequiredService<IOptions<MoexOptions>>(),
            sp.GetRequiredService<ILogger<AlgopackCurrentDayService>>()));

        return services;
    }
}
