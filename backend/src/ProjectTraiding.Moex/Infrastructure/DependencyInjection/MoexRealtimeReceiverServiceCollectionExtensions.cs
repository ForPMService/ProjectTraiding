using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.Receiver;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.Redis;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Регистрация приёмника реального времени: читатель инструментов, писатели курсора,
    /// покрытия, ClickHouse и Redis, а также независимые фоновые службы сделок и стакана.
    /// Отдельно от AddMoexRealtimeStorage намеренно — порядок наведём при ревизии.
    /// </summary>
    public static class MoexRealtimeReceiverServiceCollectionExtensions
    {
        public static IServiceCollection AddMoexRealtimeReceiver(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Приёмник по умолчанию выключен. При выключенном признаке фоновые службы и их
            // писатели не регистрируются вовсе — запуск API не начинает сбор. Секция читается
            // здесь тем же приёмом, что и в AddMoexClients (Get<MoexOptions>), потому что на
            // этапе построения контейнера привязанные настройки ещё недоступны.
            MoexOptions moexOptions =
                configuration.GetSection("Moex").Get<MoexOptions>() ?? new MoexOptions();
                Console.WriteLine($"[DIAG] RealtimeReceiverEnabled = {moexOptions.RealtimeReceiverEnabled}");  // временно
            if (!moexOptions.RealtimeReceiverEnabled)
                return services;

            services.AddSingleton<MoexReceiverInstrumentReader>();
            services.AddSingleton<StreamCursorWriter>();
            services.AddSingleton<StreamCoverageWriter>();
            services.AddSingleton<RealtimeLatestWriter>();

            // Прямые писатели ClickHouse: один на вид строки. Исполнитель и карты уже
            // зарегистрированы (AddMoexLoading / AddMoexRealtimeStorage).
            services.AddTransient(sp => new RealtimeRowWriter<RealtimeTradesStockDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<RealtimeTradesStockRowMap>(),
                sp.GetRequiredService<ILogger<RealtimeRowWriter<RealtimeTradesStockDTO>>>()));

            services.AddTransient(sp => new RealtimeRowWriter<RealtimeTradesFuturesDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<RealtimeTradesFuturesRowMap>(),
                sp.GetRequiredService<ILogger<RealtimeRowWriter<RealtimeTradesFuturesDTO>>>()));

            services.AddTransient(sp => new RealtimeRowWriter<RealtimeOrderbookRowDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<RealtimeOrderbookRowMap>(),
                sp.GetRequiredService<ILogger<RealtimeRowWriter<RealtimeOrderbookRowDTO>>>()));

            services.AddHostedService(sp =>
            {
                MoexOptions opt = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new TradesReceiverService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ILogger<TradesReceiverService>>(),
                    TimeSpan.FromSeconds(opt.TradesPollSeconds),
                    opt.RealtimeInstrumentFetchTimeout,
                    TimeSpan.FromSeconds(opt.HeartbeatMinIntervalSeconds));
            });

            services.AddHostedService(sp =>
            {
                MoexOptions opt = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new OrderbookReceiverService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ILogger<OrderbookReceiverService>>(),
                    TimeSpan.FromSeconds(opt.OrderbookPollSeconds),
                    opt.RealtimeInstrumentFetchTimeout,
                    TimeSpan.FromSeconds(opt.HeartbeatMinIntervalSeconds));
            });

            return services;
        }
    }
}
