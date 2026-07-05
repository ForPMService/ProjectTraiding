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

            return services;
        }
    }
}
