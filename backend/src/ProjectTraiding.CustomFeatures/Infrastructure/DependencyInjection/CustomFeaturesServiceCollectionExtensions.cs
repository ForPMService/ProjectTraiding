using ProjectTraiding.CustomFeatures.StorageBase.Postgres;
using ProjectTraiding.CustomFeatures.Loading;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectTraiding.Moex.Realtime.Subscriptions;

namespace ProjectTraiding.CustomFeatures.Infrastructure.DependencyInjection
{
    public static class CustomFeaturesServiceCollectionExtensions
    {
        public static IServiceCollection AddCustomFeatures(this IServiceCollection services)
        {
            services.TryAddSingleton<CandleSubscriptionRegistry>();
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
