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
            services.AddTransient<BrokerTariffWriter>();
            services.AddTransient<LoadTaskWriter>();
            return services;
        }
    }
}
