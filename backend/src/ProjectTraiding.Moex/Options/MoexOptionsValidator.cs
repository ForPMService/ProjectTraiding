using ProjectTraiding.Moex.Clients;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Options
{
    /// <summary>
    /// Проверка настроек MOEX при запуске и запись действующих значений в журнал.
    /// Без применения отражения: значения читаются напрямую с полей. Недопустимая
    /// конфигурация обрывает старт понятной ошибкой, а не проявляется сбоем в разгар
    /// загрузки. Ключ доступа в журнал не пишется — только признак его наличия.
    /// </summary>
    public static class MoexOptionsValidator
    {
        public static void ValidateAndLog(MoexOptions options, ILogger logger)
        {
            if (options.AttemptTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("Moex:AttemptTimeout должен быть положительным.");

            if (options.TotalRequestTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("Moex:TotalRequestTimeout должен быть положительным.");

            if (options.BodyReadTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("Moex:BodyReadTimeout должен быть положительным.");

            if (options.RateLimitAcquireTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("Moex:RateLimitAcquireTimeout должен быть положительным.");

            // Иерархия тайм-аутов: ожидание жетона ≤ одна попытка ≤ полный бюджет запроса.
            // Нарушение — ошибка конфигурации, обрывающая старт (правило иерархии времени).
            if (options.RateLimitAcquireTimeout > options.AttemptTimeout)
                throw new InvalidOperationException(
                    "Moex:RateLimitAcquireTimeout не может превышать Moex:AttemptTimeout.");

            if (options.AttemptTimeout > options.TotalRequestTimeout)
                throw new InvalidOperationException(
                    "Moex:AttemptTimeout не может превышать Moex:TotalRequestTimeout.");

            if (options.MaxRequestsPerSecond is < 1 or > 10)
                throw new InvalidOperationException(
                    "Moex:MaxRequestsPerSecond должен быть в диапазоне от 1 до 10.");

            if (options.MaxConnectionsPerServer <= 0)
                throw new InvalidOperationException("Moex:MaxConnectionsPerServer должен быть положительным.");

            if (options.MaxPagesPerLoad <= 0)
                throw new InvalidOperationException("Moex:MaxPagesPerLoad должен быть положительным.");

            if (options.PollIntervalSeconds <= 0)
                throw new InvalidOperationException("Moex:PollIntervalSeconds должен быть положительным.");

            if (options.LoadWorkerConcurrency <= 0)
                throw new InvalidOperationException("Moex:LoadWorkerConcurrency должен быть положительным.");

            if (options.LoadWorkerConcurrency > options.MaxConnectionsPerServer)
                throw new InvalidOperationException(
                    "Moex:LoadWorkerConcurrency не должен превышать Moex:MaxConnectionsPerServer.");

            // Действующие значения — в журнал. Ключ доступа НЕ пишем, только признак наличия.
            MoexLogMessages.OptionsApplied(
                logger,
                options.AttemptTimeout,
                options.TotalRequestTimeout,
                options.BodyReadTimeout,
                options.PollIntervalSeconds,
                options.MaxRequestsPerSecond,
                options.MaxConnectionsPerServer,
                options.MaxPagesPerLoad,
                options.LoadWorkerConcurrency,
                !string.IsNullOrWhiteSpace(options.AlgKey));
        }
    }
}
