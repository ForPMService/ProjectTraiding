using Microsoft.Extensions.DependencyInjection;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using ProjectTraiding.Vitrine.StorageBase.Redis;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Vitrine.DependencyInjection
{
    public static class VitrineServiceCollectionExtensions
    {
        public static IServiceCollection AddVitrine(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddTransient<InstrumentReadQuery>();
            services.AddTransient<CalendarReadQuery>();
            services.AddTransient<BrokerTariffReadQuery>();
            services.AddTransient<InstrumentRelationReadQuery>();
            services.AddTransient<StockCardReadQuery>();
            services.AddTransient<FuturesCardReadQuery>();
            services.AddSingleton<CatalogEventReader>();
            services.AddSingleton<TariffEventReader>();
            services.AddTransient<InstrumentRelationBySecidReadQuery>();
            services.AddTransient<StatusReadQuery>();

            // Срок жизни ключа справочника — страховка на случай потерянного события.
            // Основную свежесть даёт событие catalog:changed (добавляется следующими шагами).
            // По умолчанию сутки; переопределяется настройкой Vitrine:InstrumentsCacheTtl.
            string ttlRaw = configuration["Vitrine:InstrumentsCacheTtl"] ?? "1.00:00:00";
            TimeSpan cacheTtl = TimeSpan.Parse(ttlRaw, CultureInfo.InvariantCulture);

            services.AddTransient<InstrumentCatalogCache>(sp => new InstrumentCatalogCache(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<InstrumentReadQuery>(),
                sp.GetRequiredService<ILogger<InstrumentCatalogCache>>(),
                cacheTtl));

            // Кеш тарифов — тот же образец «чтение через кеш», свой ключ и свой срок жизни.
            string tariffsTtlRaw = configuration["Vitrine:TariffsCacheTtl"] ?? "1.00:00:00";
            TimeSpan tariffsCacheTtl = TimeSpan.Parse(tariffsTtlRaw, CultureInfo.InvariantCulture);

            services.AddTransient<BrokerTariffCache>(sp => new BrokerTariffCache(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<BrokerTariffReadQuery>(),
                sp.GetRequiredService<ILogger<BrokerTariffCache>>(),
                tariffsCacheTtl));

            string pollRaw = configuration["Vitrine:CatalogPollInterval"] ?? "00:00:02";
            TimeSpan catalogPoll = TimeSpan.Parse(pollRaw, CultureInfo.InvariantCulture);

            services.AddHostedService(sp => new CatalogEventListener(
                sp.GetRequiredService<CatalogEventReader>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<CatalogEventListener>>(),
                catalogPoll));

            // Слушатель потока изменения тарифов — тот же образец, свой интервал опроса.
            string tariffPollRaw = configuration["Vitrine:TariffPollInterval"] ?? "00:00:02";
            TimeSpan tariffPoll = TimeSpan.Parse(tariffPollRaw, CultureInfo.InvariantCulture);

            services.AddHostedService(sp => new TariffEventListener(
                sp.GetRequiredService<TariffEventReader>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<TariffEventListener>>(),
                tariffPoll));
            return services;
        }
    }
}
