using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.Receiver;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Регистрация включаемого контура реального времени: карты столбцов, читатель инструментов,
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

            services.AddSingleton<RealtimeTradesStockRowMap>();
            services.AddSingleton<RealtimeTradesFuturesRowMap>();
            services.AddSingleton<RealtimeOrderbookRowMap>();

            services.AddSingleton<MoexReceiverInstrumentReader>();
            services.AddSingleton<StreamCursorWriter>();
            services.AddSingleton<StreamCoverageWriter>();

            // Прямые писатели ClickHouse: один на вид строки. Исполнитель зарегистрирован
            // исторической загрузкой, карты — выше в этой включённой ветви.
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

            // Свечной писатель приёма: та же карта, что у истории, но приоритет 0 —
            // историческая загрузка (приоритет 1) перекрывает при слиянии по ключу (secid, begin).
            services.AddTransient(sp => new RealtimeRowWriter<CandlesDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                new CandlesRowMap("moex_candles_1m", "candles:1m", ingestPriority: 0),
                sp.GetRequiredService<ILogger<RealtimeRowWriter<CandlesDTO>>>()));

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
