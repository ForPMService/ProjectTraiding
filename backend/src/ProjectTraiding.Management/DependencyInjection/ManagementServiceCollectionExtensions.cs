using ProjectTraiding.Management.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.DependencyInjection
{
    public static class ManagementServiceCollectionExtensions
    {
        public static IServiceCollection AddManagement(this IServiceCollection services)
        {
            services.AddTransient<InstrumentRelationWriter>();
            services.AddTransient<LoadTaskWriter>();
            services.AddTransient<RealtimeSubscriptionWriter>();
            services.AddTransient<ManualEventWriter>();
            services.AddTransient<TradingPeriodWriter>();
            services.AddTransient<TradingPeriodTypeWriter>();
            return services;
        }
    }
}
