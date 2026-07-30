using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.Redis;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Регистрация всего контура исторической загрузки: исполнитель вставки, писатели по видам
    /// и словарь свечных писателей по интервалам, карты столбцов, обработчики видов, читатель и
    /// писатели задач, диспетчер, координатор и фоновый исполнитель. Вынесено из точки входа,
    /// чтобы та оставалась тонкой. Размер пачки берётся из настроек (ClickHouse:CandlesBatchSize).
    /// </summary>
    public static class MoexLoadingServiceCollectionExtensions
    {
        public static IServiceCollection AddMoexLoading(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            int batchSize = configuration.GetValue<int>("ClickHouse:CandlesBatchSize", 10000);

            services.AddTransient<ClickHouseInsertExecutor>();

            // Свечные писатели по интервалам: одна форма, разные таблица и префикс токена.
            // Коды интервала MOEX: 1 — минута, 10 — десять минут, 60 — час, 24 — день.
            services.AddSingleton<IReadOnlyDictionary<int, RowWriter<CandlesDTO>>>(sp =>
            {
                ClickHouseInsertExecutor executor = sp.GetRequiredService<ClickHouseInsertExecutor>();
                ILogger<RowWriter<CandlesDTO>> logger = sp.GetRequiredService<ILogger<RowWriter<CandlesDTO>>>();

                RowWriter<CandlesDTO> Make(string table, string tokenPrefix) =>
                    new RowWriter<CandlesDTO>(
                        executor, new CandlesRowMap(table, tokenPrefix, ingestPriority: 1), logger, batchSize);

                return new Dictionary<int, RowWriter<CandlesDTO>>
                {
                    [1] = Make("moex_candles_1m", "candles:1m"),
                    [10] = Make("moex_candles_10m", "candles:10m"),
                    [60] = Make("moex_candles_1h", "candles:1h"),
                    [24] = Make("moex_candles_1d", "candles:1d"),
                };
            });

            // Карты столбцов остальных видов — без состояния, одиночки.
            services.AddSingleton<TradeStatsStockRowMap>();
            services.AddSingleton<TradeStatsFuturesRowMap>();
            services.AddSingleton<ObStatsStockRowMap>();
            services.AddSingleton<ObStatsFuturesRowMap>();
            services.AddSingleton<OrderStatsStockRowMap>();
            services.AddSingleton<FutoiRowMap>();
            services.AddSingleton<Hi2StockRowMap>();
            services.AddSingleton<Hi2FuturesRowMap>();
            services.AddSingleton<MegaAlertsStockRowMap>();
            services.AddSingleton<MegaAlertsFuturesRowMap>();

            // Писатели под каждый вид (тот же размер пачки, что у свечей).
            services.AddTransient(sp => new RowWriter<SuperCandlesTradeStats5mDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<TradeStatsStockRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<SuperCandlesTradeStats5mDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<SuperCandlesFuturesTradeStats5mDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<TradeStatsFuturesRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<SuperCandlesFuturesTradeStats5mDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<SuperCandlesOrderBookStats5mDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<ObStatsStockRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<SuperCandlesOrderBookStats5mDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<SuperCandlesFuturesOrderBookStats5mDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<ObStatsFuturesRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<SuperCandlesFuturesOrderBookStats5mDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<SuperCandlesOrderStats5mDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<OrderStatsStockRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<SuperCandlesOrderStats5mDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<FutoiDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<FutoiRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<FutoiDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<Hi2AssetDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<Hi2StockRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<Hi2AssetDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<Hi2FuturesDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<Hi2FuturesRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<Hi2FuturesDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<MegaAlertsAssetsDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<MegaAlertsStockRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<MegaAlertsAssetsDTO>>>(), batchSize));
            services.AddTransient(sp => new RowWriter<MegaAlertsFuturesDTO>(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<MegaAlertsFuturesRowMap>(),
                sp.GetRequiredService<ILogger<RowWriter<MegaAlertsFuturesDTO>>>(), batchSize));

            // Обработчики видов — диспетчер соберёт их все через IEnumerable<ILoadHandler>.
            // Девять курсорных видов закрыты обобщённым обработчиком парой «паспорт × строка»;
            // FUTOI и свечи остаются отдельными обработчиками из-за иной формы загрузки.
            services.AddScoped<ILoadHandler, SeriesLoadHandler<TradeStatsStockKind, SuperCandlesTradeStats5mDTO>>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<TradeStatsFuturesKind, SuperCandlesFuturesTradeStats5mDTO>>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<ObStatsStockKind, SuperCandlesOrderBookStats5mDTO>>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<ObStatsFuturesKind, SuperCandlesFuturesOrderBookStats5mDTO>>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<OrderStatsStockKind, SuperCandlesOrderStats5mDTO>>();
            services.AddScoped<ILoadHandler, FutoiLoadHandler>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<Hi2StockKind, Hi2AssetDTO>>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<Hi2FuturesKind, Hi2FuturesDTO>>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<MegaAlertsStockKind, MegaAlertsAssetsDTO>>();
            services.AddScoped<ILoadHandler, SeriesLoadHandler<MegaAlertsFuturesKind, MegaAlertsFuturesDTO>>();
            services.AddScoped<ILoadHandler, CandlesLoadHandler>();

            // Читатель и писатели задач, диспетчер и координатор.
            services.AddTransient<MoexLoadTaskReader>();
            services.AddTransient<MoexLoadTaskWriter>();
            services.AddTransient<MoexLoadedRangeWriter>();
            services.AddSingleton<ProjectTraiding.Moex.StorageBase.Redis.LoadedRangeEventPublisher>();
            services.AddScoped<LoadHandlerDispatcher>();
            services.AddScoped<LoadRunner>();

            // Приёмник хода загрузки: писатель прогресса в оперативное хранилище.
            // Срок жизни ключа прогресса — самоочистка; по умолчанию сутки,
            // переопределяется настройкой Moex:LoadProgressTtl.
            string progressTtlRaw = configuration["Moex:LoadProgressTtl"] ?? "1.00:00:00";
            TimeSpan progressTtl = TimeSpan.Parse(progressTtlRaw, CultureInfo.InvariantCulture);

            services.AddSingleton<ILoadProgressReporter>(sp => new LoadProgressWriter(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<ILogger<LoadProgressWriter>>(),
                progressTtl));

            // Фоновый исполнитель: интервал опроса и число дорожек — из настроек.
            services.AddHostedService(sp =>
            {
                MoexOptions moexOptions = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
                return new MoexLoadBackgroundService(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ILogger<MoexLoadBackgroundService>>(),
                    TimeSpan.FromSeconds(moexOptions.PollIntervalSeconds),
                    moexOptions.LoadWorkerConcurrency);
            });

            return services;
        }
    }
}
