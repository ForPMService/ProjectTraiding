using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.Receiver;
using ProjectTraiding.Moex.Realtime.Subscriptions;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Регистрация включаемого контура реального времени: читатель инструментов,
    /// писатели курсора и ClickHouse, а также независимые фоновые службы
    /// сделок, стакана и свечей.
    /// </summary>
    public static class MoexRealtimeReceiverServiceCollectionExtensions
    {
        public static IServiceCollection AddMoexRealtimeReceiver(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Писатель подписок регистрируется безусловно: операторские команды включения
            // и отключения работают и при выключенном приёмнике.
            services.AddTransient<RealtimeSubscriptionWriter>();

            // Приёмник по умолчанию выключен. При выключенном признаке фоновые службы и их
            // зависимые писатели не регистрируются — запуск API не начинает сбор. Секция
            // читается здесь тем же приёмом, что и в AddMoexClients (Get<MoexOptions>), потому
            // что на этапе построения контейнера привязанные настройки ещё недоступны.
            MoexOptions moexOptions =
                configuration.GetSection("Moex").Get<MoexOptions>() ?? new MoexOptions();
            if (!moexOptions.RealtimeReceiverEnabled)
                return services;

            services.AddSingleton<MoexReceiverInstrumentReader>();
            services.AddSingleton<StreamCursorWriter>();
            int candlesBatchSize = configuration.GetValue<int>(
                "ClickHouse:CandlesBatchSize", 10_000);

            services.AddHostedService(sp =>
            {
                MoexOptions opt = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new TradesReceiverService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ILogger<TradesReceiverService>>(),
                    TimeSpan.FromSeconds(opt.TradesPollSeconds),
                    opt.RealtimeInstrumentFetchTimeout,
                    TimeSpan.FromSeconds(
                        opt.TradesPollSeconds * (double)opt.RealtimeStalePollIntervals));
            });

            services.AddHostedService(sp =>
            {
                MoexOptions opt = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new OrderbookReceiverService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ILogger<OrderbookReceiverService>>(),
                    TimeSpan.FromSeconds(opt.OrderbookPollSeconds),
                    opt.RealtimeInstrumentFetchTimeout,
                    TimeSpan.FromSeconds(
                        opt.OrderbookPollSeconds * (double)opt.RealtimeStalePollIntervals));
            });

            services.AddHostedService(sp =>
            {
                MoexOptions opt = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new CandlesReceiverService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<CandleSubscriptionRegistry>(),
                    sp.GetRequiredService<ILogger<CandlesReceiverService>>(),
                    TimeSpan.FromSeconds(opt.CandlesPollSeconds),
                    opt.RealtimeInstrumentFetchTimeout,
                    TimeSpan.FromSeconds(
                        opt.CandlesPollSeconds * (double)opt.RealtimeStalePollIntervals),
                    candlesBatchSize,
                    opt.MaxPagesPerLoad);
            });

            return services;
        }
    }
}
