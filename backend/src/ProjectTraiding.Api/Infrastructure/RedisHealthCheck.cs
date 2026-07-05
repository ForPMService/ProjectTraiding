using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ProjectTraiding.Api.Infrastructure
{
    // <summary>
    /// Минимальная проверка готовности оперативного хранилища: команда PING (запрос «ты жив»).
    /// Ответ — хранилище доступно; исключение — недоступно, состояние «нездоров».
    /// </summary>
    public sealed class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _multiplexer;

        public RedisHealthCheck(IConnectionMultiplexer multiplexer)
        {
            _multiplexer = multiplexer;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _multiplexer.GetDatabase().PingAsync();
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Оперативное хранилище недоступно.", ex);
            }
        }
    }
}
