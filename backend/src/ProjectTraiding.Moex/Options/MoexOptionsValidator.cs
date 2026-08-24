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

            if (string.IsNullOrWhiteSpace(options.CertificatesDirectory))
                throw new InvalidOperationException("Moex:CertificatesDirectory не может быть пустым.");

            if (options.MaxPagesPerLoad <= 0)
                throw new InvalidOperationException("Moex:MaxPagesPerLoad должен быть положительным.");

            if (options.PollIntervalSeconds <= 0)
                throw new InvalidOperationException("Moex:PollIntervalSeconds должен быть положительным.");

            if (options.TradesPollSeconds <= 0)
                throw new InvalidOperationException("Moex:TradesPollSeconds должен быть положительным.");

            if (options.OrderbookPollSeconds <= 0)
                throw new InvalidOperationException("Moex:OrderbookPollSeconds должен быть положительным.");

            if (options.CandlesPollSeconds <= 0)
                throw new InvalidOperationException("Moex:CandlesPollSeconds должен быть положительным.");

            if (options.RealtimeStalePollIntervals <= 0)
                throw new InvalidOperationException("Moex:RealtimeStalePollIntervals должен быть положительным.");

            if (options.RealtimeInstrumentFetchTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("Moex:RealtimeInstrumentFetchTimeout должен быть положительным.");

            if (options.HeartbeatMinIntervalSeconds <= 0)
                throw new InvalidOperationException("Moex:HeartbeatMinIntervalSeconds должен быть положительным.");

            if (options.LoadWorkerConcurrency <= 0)
                throw new InvalidOperationException("Moex:LoadWorkerConcurrency должен быть положительным.");

            if (options.LoadWorkerConcurrency > options.MaxConnectionsPerServer)
                throw new InvalidOperationException(
                    $"Moex:LoadWorkerConcurrency ({options.LoadWorkerConcurrency}) не должен превышать " +
                    $"Moex:MaxConnectionsPerServer ({options.MaxConnectionsPerServer}).");

            // Действующие значения — в журнал. Ключ доступа НЕ пишем, только признак наличия.
            MoexLogMessages.OptionsApplied(
                logger,
                options.AttemptTimeout,
                options.TotalRequestTimeout,
                options.BodyReadTimeout,
                options.PollIntervalSeconds,
                options.RealtimeInstrumentFetchTimeout,
                options.HeartbeatMinIntervalSeconds,
                options.MaxRequestsPerSecond,
                options.MaxConnectionsPerServer,
                options.MaxPagesPerLoad,
                options.LoadWorkerConcurrency,
                !string.IsNullOrWhiteSpace(options.AlgKey));
        }
    }
}
