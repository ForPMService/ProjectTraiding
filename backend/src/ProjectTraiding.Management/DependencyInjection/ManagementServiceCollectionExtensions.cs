using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Management.Deletion;
using ProjectTraiding.Management.StorageBase.ClickHouse;
using ProjectTraiding.Management.StorageBase.Redis;
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
            services.AddTransient<TariffEventPublisher>();
            services.AddTransient<InstrumentDeletionGuardReader>();
            services.AddTransient<InstrumentDeletionWriter>();
            services.AddTransient<InstrumentPostgresDataDeleter>();
            services.AddTransient<InstrumentClickHouseDataDeleter>();
            services.AddTransient<InstrumentRedisDataDeleter>();
            services.AddTransient<InstrumentDataDeletionRunner>();
            return services;
        }
    }
}
