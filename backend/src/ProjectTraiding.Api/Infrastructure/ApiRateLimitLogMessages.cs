namespace ProjectTraiding.Api.Infrastructure
{
    /// <summary>
    /// Лог-события ограничителя частоты входящих запросов.
    /// EventId 500–509: зарезервировано за api-rate-limit.
    /// Отказ — Debug: счёт отказов ведёт метрика api.ratelimit.rejected,
    /// запись журнала нужна лишь при разборе конкретного случая и добавляет к метрике
    /// единственное сведение — запрошенный путь.
    /// </summary>
    public static partial class ApiRateLimitLogMessages
    {
        [LoggerMessage(EventId = 500, EventName = "ApiRateLimitRequestRejected", Level = LogLevel.Debug,
            Message = "Rate limit rejected request: group={Group}, path={Path}.")]
        public static partial void RequestRejected(ILogger logger, string group, string path);
    }
}
