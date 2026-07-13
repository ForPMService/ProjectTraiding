using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Регистрация слоя записи контура реального времени: карты столбцов и писатели ленты сделок
    /// и стакана. Свечи реального времени идут в существующий свечной писатель — здесь их нет.
    /// Размер пачки берётся из той же настройки, что у исторической загрузки.
    /// </summary>
    public static class MoexRealtimeStorageServiceCollectionExtensions
    {
        public static IServiceCollection AddMoexRealtimeStorage(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            int batchSize = configuration.GetValue<int>("ClickHouse:CandlesBatchSize", 10000);

            services.AddSingleton<RealtimeTradesStockRowMap>();
            services.AddSingleton<RealtimeTradesFuturesRowMap>();
            services.AddSingleton<RealtimeOrderbookRowMap>();

            services.AddTransient(sp => new RowWriter<RealtimeTradesStockDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<RealtimeTradesStockRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<RealtimeTradesStockDTO>>>(), batchSize));

            services.AddTransient(sp => new RowWriter<RealtimeTradesFuturesDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<RealtimeTradesFuturesRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<RealtimeTradesFuturesDTO>>>(), batchSize));

            services.AddTransient(sp => new RowWriter<RealtimeOrderbookRowDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<RealtimeOrderbookRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<RealtimeOrderbookRowDTO>>>(), batchSize));

            return services;
        }
    }
}
