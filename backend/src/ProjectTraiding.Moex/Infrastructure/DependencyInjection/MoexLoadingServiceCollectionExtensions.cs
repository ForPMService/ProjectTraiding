using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Регистрация всего контура исторической загрузки: исполнитель вставки, писатели по видам
    /// и карты столбцов, читатель и писатели задач, координатор и фоновый исполнитель.
    /// Вынесено из точки входа,
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
            services.AddSingleton<MoexSeriesParser>();
            services.AddTransient<MoexHistoryPageReader>();
            services.AddTransient(sp => new MoexHistoryWriter(
                sp.GetRequiredService<ClickHouseInsertExecutor>(),
                sp.GetRequiredService<ILogger<MoexHistoryWriter>>(),
                batchSize));

            services.AddScoped<SpecLoadHandler>();

            // Читатель и писатели задач, диспетчер и координатор.
            services.AddTransient<MoexLoadTaskReader>();
            services.AddTransient<MoexLoadTaskWriter>();
            services.AddTransient<MoexLoadedRangeWriter>();
            services.AddSingleton<ProjectTraiding.Moex.StorageBase.Redis.LoadedRangeEventPublisher>();
            services.AddScoped<LoadRunner>();

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
