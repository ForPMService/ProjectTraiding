using Microsoft.Extensions.DependencyInjection;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.DependencyInjection
{
    public static class VitrineServiceCollectionExtensions
    {
        public static IServiceCollection AddVitrine(this IServiceCollection services)
        {
            services.AddTransient<InstrumentReadQuery>();
            return services;
        }
    }
}
