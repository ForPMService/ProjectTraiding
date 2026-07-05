using StackExchange.Redis;

namespace ProjectTraiding.Api.Infrastructure
{
    public static class RedisService
    {
        public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
        {
            string connectionString = configuration.GetConnectionString("Redis");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'Redis' not found.");
            }

            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
                options.Protocol = RedisProtocol.Resp3;   // новый протокол обмена — явно и сразу
                options.AbortOnConnectFail = false;        // недоступность хранилища не валит старт: идёт фоновое переподключение
                return ConnectionMultiplexer.Connect(options);
            });

            // Проверка готовности заводится рядом с регистрацией клиента: владелец фабрики владеет
            // и проверкой. Повторный вызов AddHealthChecks() возвращает тот же построитель проверок,
            // поэтому базовый механизм из модуля наблюдаемости не ломается.
            services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis");

            return services;
        }
    }
}
