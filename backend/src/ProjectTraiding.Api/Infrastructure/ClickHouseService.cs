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
            // Регистрация фабрикой, а не готовым экземпляром: так контейнер владеет жизненным
            // циклом единственного клиента и освобождает его при остановке приложения.
            services.AddSingleton<ClickHouseClient>(_ => new ClickHouseClient(connectionString));
            return services;
        }
    }
}
