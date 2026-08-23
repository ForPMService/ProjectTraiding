using Npgsql;

namespace ProjectTraiding.Management.Endpoints
{
    /// <summary>
    /// Перевод кода состояния PostgreSQL в текст ответа оператору. Один набор кодов —
    /// один метод: набор кодов является частью контракта маршрута, а не общим списком.
    /// Возврат null означает, что код неизвестен и исключение обязано уйти дальше.
    /// Запись в журнал выполняется здесь, чтобы каждый вызывающий не повторял её.
    /// </summary>
    internal static class ManagementDbErrors
    {
        private const string SecidNotFound = "secid не найден среди инструментов (FK)";

        /// <summary>Маршруты приёма реального времени: единственное нарушение — внешний ключ.</summary>
        internal static string? MapSubscription(ILogger logger, string route, PostgresException ex)
        {
            ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
            return ex.SqlState switch
            {
                "23503" => SecidNotFound,
                _ => null
            };
        }

        /// <summary>Маршруты заданий загрузки: внешний ключ и страховочное ограничение значений.</summary>
        internal static string? MapLoadTask(ILogger logger, string route, PostgresException ex)
        {
            ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
            return ex.SqlState switch
            {
                "23503" => SecidNotFound,
                "23514" => "недопустимое значение market или storage_target (страховка)",
                _ => null
            };
        }

        /// <summary>Маршрут тарифов: единственное нарушение — страховочное ограничение рынка.</summary>
        internal static string? MapTariff(ILogger logger, string route, PostgresException ex)
        {
            ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
            return ex.SqlState switch
            {
                "23514" => "market должен быть одним из: stock, futures (страховка)",
                _ => null
            };
        }

        /// <summary>
        /// Маршрут связей инструментов. Коды инструментов входят в текст ответа, поэтому
        /// передаются параметрами, а не читаются из исключения.
        ///
        /// Только внешний ключ: проверочные ограничения перекрыты валидатором целиком
        /// (словари relation_type и confidence, инвариант «нужен хотя бы один target»),
        /// а уникальность недостижима — писатель вставляет через ON CONFLICT DO UPDATE.
        /// </summary>
        internal static string? MapRelation(
            ILogger logger,
            string route,
            PostgresException ex,
            string? sourceSecid,
            string? targetSecid)
        {
            ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
            return ex.SqlState switch
            {
                "23503" => $"инструмент не найден (source_secid={sourceSecid}, target_secid={targetSecid})",
                _ => null
            };
        }

        /// <summary>
        /// Постановка заявки на удаление данных инструмента. Существование инструмента
        /// проверяет внешний ключ таблицы заявок, отдельной проверки перед вставкой нет.
        /// </summary>
        internal static string? MapDeletion(ILogger logger, string route, PostgresException ex)
        {
            ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
            return ex.SqlState switch
            {
                "23503" => "инструмент не найден",
                _ => null
            };
        }
    }
}
