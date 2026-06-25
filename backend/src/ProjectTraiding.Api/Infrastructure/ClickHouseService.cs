using ClickHouse.Driver;
namespace ProjectTraiding.Api.Infrastructure
{
    public static class ClickHouseService
    {
        public static IServiceCollection AddClickHouse(this IServiceCollection services, IConfiguration configuration)
        {
            string connectionString = configuration.GetConnectionString("ClickHouseDb");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'ClickHouseDb' not found.");
            }
            ClickHouseClient connection = new ClickHouseClient(connectionString);
            services.AddSingleton(connection);
            return services;
        }
    }
}
