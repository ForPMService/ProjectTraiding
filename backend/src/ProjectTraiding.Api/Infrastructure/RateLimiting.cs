using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ProjectTraiding.Api.Infrastructure
{
    /// <summary>
    /// Ограничитель частоты входящих запросов. Две политики маркерного ведра:
    /// витрина (частый публичный поток) и управление (редкие команды оператора).
    /// Ёмкость — прощаемый всплеск (загрузка страницы плюс сетевые повторы);
    /// пополнение — устойчивый темп. Очередь отключена: пустое ведро — сразу 429,
    /// без удержания соединения. Ключ — адрес клиента, у каждого своё ведро.
    /// Обработчик отказа — статический метод (запрет делегатов на горячем пути).
    /// </summary>
    public static class RateLimiting
    {
        public const string VitrinePolicy = "vitrine";
        public const string ManagementPolicy = "management";

        public static void AddProjectTraidingRateLimiter(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.AddPolicy(VitrinePolicy, PartitionByClient(
                    tokenLimit: 400,
                    tokensPerPeriod: 1,
                    replenishmentSeconds: 2));

                options.AddPolicy(ManagementPolicy, PartitionByClient(
                    tokenLimit: 300,
                    tokensPerPeriod: 1,
                    replenishmentSeconds: 2));

                options.OnRejected = OnRejectedStatic;
            });
        }

        private static Func<HttpContext, RateLimitPartition<string>> PartitionByClient(
            int tokenLimit, int tokensPerPeriod, int replenishmentSeconds)
        {
            TimeSpan period = TimeSpan.FromSeconds(replenishmentSeconds);
            return context => RateLimitPartition.GetTokenBucketLimiter(
                partitionKey: ClientKey(context),
                factory: _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = tokenLimit,
                    TokensPerPeriod = tokensPerPeriod,
                    ReplenishmentPeriod = period,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                });
        }

        private static string ClientKey(HttpContext context)
        {
            string? forwarded = context.Request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty(forwarded))
            {
                int comma = forwarded.IndexOf(',');
                return comma > 0 ? forwarded[..comma].Trim() : forwarded.Trim();
            }

            System.Net.IPAddress? ip = context.Connection.RemoteIpAddress;
            return ip is not null ? ip.ToString() : "unknown";
        }

        private static ValueTask OnRejectedStatic(OnRejectedContext context, CancellationToken ct)
        {
            HttpContext http = context.HttpContext;
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            string group = http.Request.Path.StartsWithSegments("/management")
                ? ManagementPolicy
                : VitrinePolicy;

            ILogger logger = http.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectTraiding.Api.RateLimiting");
            ApiRateLimitLogMessages.RequestRejected(logger, group, http.Request.Path.Value ?? "unknown");

            ApiMetrics.RateLimitRejected.Add(1,
                new KeyValuePair<string, object?>("group", group));

            return ValueTask.CompletedTask;
        }
    }
}
