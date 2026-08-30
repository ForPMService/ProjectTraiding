using ProjectTraiding.CustomFeatures.StorageBase.Postgres;

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
            return services;
        }
    }
}
