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

            if (options.TotalRequestTimeout < options.AttemptTimeout)
                throw new InvalidOperationException(
                    "Moex:TotalRequestTimeout не может быть меньше Moex:AttemptTimeout.");

            if (options.BodyReadTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("Moex:BodyReadTimeout должен быть положительным.");

            if (options.RequestTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("Moex:RequestTimeout должен быть положительным.");

            if (options.MaxRequestsPerSecond <= 0)
                throw new InvalidOperationException("Moex:MaxRequestsPerSecond должен быть положительным.");

            if (options.MaxConnectionsPerServer <= 0)
                throw new InvalidOperationException("Moex:MaxConnectionsPerServer должен быть положительным.");

            if (options.MaxPagesPerLoad <= 0)
                throw new InvalidOperationException("Moex:MaxPagesPerLoad должен быть положительным.");

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
                options.MaxRequestsPerSecond,
                options.MaxConnectionsPerServer,
                options.MaxPagesPerLoad,
                options.LoadWorkerConcurrency,
                !string.IsNullOrWhiteSpace(options.AlgKey));
        }
    }
}
