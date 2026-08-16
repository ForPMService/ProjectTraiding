using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.Receiver;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Регистрация включаемого контура реального времени: читатель инструментов,
    /// писатели курсора, покрытия и ClickHouse, а также независимые фоновые службы
    /// сделок, стакана и свечей.
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
            if (!moexOptions.RealtimeReceiverEnabled)
                return services;

            services.AddSingleton<MoexReceiverInstrumentReader>();
            services.AddSingleton<StreamCursorWriter>();
            services.AddSingleton<StreamCoverageWriter>();
            services.AddTransient<RealtimeSpecRowWriter>();

            services.AddHostedService(sp =>
            {
                MoexOptions opt = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new TradesReceiverService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ILogger<TradesReceiverService>>(),
                    TimeSpan.FromSeconds(opt.TradesPollSeconds),
                    opt.RealtimeInstrumentFetchTimeout,
                    TimeSpan.FromSeconds(opt.HeartbeatMinIntervalSeconds),
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
                    TimeSpan.FromSeconds(opt.HeartbeatMinIntervalSeconds),
                    TimeSpan.FromSeconds(
                        opt.OrderbookPollSeconds * (double)opt.RealtimeStalePollIntervals));
            });

            services.AddHostedService(sp =>
            {
                MoexOptions opt = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new CandlesReceiverService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ILogger<CandlesReceiverService>>(),
                    TimeSpan.FromSeconds(opt.CandlesPollSeconds),
                    opt.RealtimeInstrumentFetchTimeout,
                    TimeSpan.FromSeconds(opt.HeartbeatMinIntervalSeconds),
                    TimeSpan.FromSeconds(
                        opt.CandlesPollSeconds * (double)opt.RealtimeStalePollIntervals));
            });

            return services;
        }
    }
}
