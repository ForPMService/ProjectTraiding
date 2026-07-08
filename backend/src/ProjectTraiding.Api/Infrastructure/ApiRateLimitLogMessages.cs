namespace ProjectTraiding.Api.Infrastructure
{
    /// <summary>
    /// Лог-события ограничителя частоты входящих запросов.
    /// EventId 500–509: зарезервировано за api-rate-limit.
    /// Отказ — Information: штатное срабатывание защиты, не ошибка приложения.
    /// </summary>
    public static partial class ApiRateLimitLogMessages
    {
        [LoggerMessage(EventId = 500, EventName = "ApiRateLimitRequestRejected", Level = LogLevel.Information,
            Message = "Rate limit rejected request: group={Group}, path={Path}.")]
        public static partial void RequestRejected(ILogger logger, string group, string path);
    }
}
