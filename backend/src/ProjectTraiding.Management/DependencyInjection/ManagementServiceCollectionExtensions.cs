using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Management.Deletion;
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
            services.AddTransient<BrokerTariffWriter>();
            services.AddTransient<LoadTaskWriter>();
            services.AddTransient<FutoiSubjectReader>();
            services.AddTransient<LoadedRangeCoverageReader>();
            services.AddTransient<RealtimeSubscriptionWriter>();
            services.AddTransient<ManualEventWriter>();
            services.AddTransient<TradingPeriodWriter>();
            services.AddTransient<TradingPeriodTypeWriter>();
            services.AddTransient<InstrumentDeletionGuardReader>();
            services.AddTransient<InstrumentDeletionWriter>();
            services.AddTransient<InstrumentDataDeletionRunner>();
            return services;
        }
    }
}
