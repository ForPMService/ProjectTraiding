using ProjectTraiding.CustomFeatures.StorageBase.Postgres;
using ProjectTraiding.CustomFeatures.Loading;

namespace ProjectTraiding.CustomFeatures.Infrastructure.DependencyInjection
{
    public static class CustomFeaturesServiceCollectionExtensions
    {
        public static IServiceCollection AddCustomFeatures(this IServiceCollection services)
        {
            services.AddTransient<BrokerTariffWriter>();
            services.AddTransient<InstrumentRelationWriter>();
            services.AddTransient<ManualEventWriter>();
            services.AddTransient<TradingPeriodWriter>();
            services.AddTransient<TradingPeriodTypeWriter>();
            services.AddTransient<CalendarDayWriter>();
            services.AddTransient<CalendarReferenceWriter>();
            services.AddTransient<CalendarLoader>();
            return services;
        }
    }
}
