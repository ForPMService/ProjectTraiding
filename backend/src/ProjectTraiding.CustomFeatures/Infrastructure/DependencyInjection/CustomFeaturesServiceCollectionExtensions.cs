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
            services.AddTransient<DividendEventWriter>();
            services.AddTransient<TradingPeriodWriter>();
            services.AddTransient<CalendarDayWriter>();
            services.AddTransient<CalendarReferenceWriter>();
            services.AddTransient<CbRateMeetingFactWriter>();
            services.AddTransient<CbRateCalendarWriter>();
            services.AddTransient<CalendarLoader>();
            return services;
        }
    }
}
