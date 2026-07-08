using System.Diagnostics.Metrics;

namespace ProjectTraiding.Api.Infrastructure
{
    /// <summary>
    /// Метрики контура Api. Пока — только отказы ограничителя частоты.
    /// Измеритель подключается к трубе наблюдаемости передачей имени
    /// в AddProjectTraidingObservability (см. Program.cs).
    /// </summary>
    public static class ApiMetrics
    {
        public const string MeterName = "ProjectTraiding.Api";

        private static readonly Meter Meter = new(MeterName);

        /// <summary>
        /// Количество отказов ограничителя частоты (ответ 429).
        /// Атрибут "group": vitrine или management.
        /// </summary>
        public static readonly Counter<long> RateLimitRejected =
            Meter.CreateCounter<long>("api.ratelimit.rejected", description: "Rate limit rejections (429) total");
    }
}
