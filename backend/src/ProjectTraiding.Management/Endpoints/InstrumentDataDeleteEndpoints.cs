using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.Deletion;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class InstrumentDataDeleteEndpointsLog;

    public static class InstrumentDataDeleteEndpoints
    {
        public static IEndpointRouteBuilder MapInstrumentDataDeleteEndpoints(
            this IEndpointRouteBuilder routes)
        {
            // Сегмент data обязателен: он говорит, что удаляются данные инструмента,
            // а не сам инструмент. Карточка, справочные детали, связи, тарифы и
            // календарь остаются на месте.
            //
            // resume — необязательный параметр строки запроса. Передаётся тогда и
            // только тогда, когда прошлая попытка удаления прервалась ошибкой и
            // оставила незакрытое задание. Тип bool?, а не bool со значением по
            // умолчанию: отсутствующий параметр должен читаться как «не продолжаем»
            // без опоры на поведение привязки при нативной компиляции.
            routes.MapPost("/management/instruments/{secid}/data/delete", async (
                string secid,
                bool? resume,
                InstrumentDataDeletionRunner runner,
                ILogger<InstrumentDataDeleteEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/instruments/{secid}/data/delete";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                if (string.IsNullOrWhiteSpace(secid))
                {
                    const string error = "secid обязателен";
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                DeletionOutcome outcome = await runner.RunAsync(secid, resume == true, ct);

                if (outcome.Status != DeletionStatus.Done)
                {
                    ManagementEndpointLogMessages.InstrumentDataDeleteRejected(
                        logger, route, secid, outcome.Status.ToString());
                }

                return outcome.Status switch
                {
                    DeletionStatus.InstrumentNotFound =>
                        Results.NotFound("инструмент не найден"),

                    DeletionStatus.LoadRunning =>
                        Results.Text(
                            "по инструменту сейчас выполняется загрузка данных, удаление невозможно",
                            "text/plain", statusCode: StatusCodes.Status409Conflict),

                    DeletionStatus.RealtimeEnabled =>
                        Results.Text(
                            "по инструменту включён приём реального времени, удаление невозможно",
                            "text/plain", statusCode: StatusCodes.Status409Conflict),

                    DeletionStatus.AlreadyDeleting =>
                        Results.Text(
                            "удаление данных этого инструмента уже начато и не закрыто. " +
                            "Если предыдущая попытка прервалась ошибкой, повторите команду " +
                            "с параметром resume=true. Сначала убедитесь, что прошлая попытка " +
                            "действительно завершилась: одновременное выполнение двух очисток " +
                            "система не предотвращает",
                            "text/plain", statusCode: StatusCodes.Status409Conflict),

                    _ => Results.Json(
                        new InstrumentDataDeleteResponse(
                            Secid: secid,
                            Status: "finished",
                            LoadedRangesDeleted: outcome.LoadedRangesDeleted,
                            StreamCursorsDeleted: outcome.StreamCursorsDeleted,
                            LoadTasksDeleted: outcome.LoadTasksDeleted,
                            ClickHouseTablesCleared: outcome.ClickHouseTablesCleared,
                            RedisKeysDeleted: outcome.RedisKeysDeleted,
                            ElapsedMs: outcome.Elapsed.TotalMilliseconds),
                        ManagementJsonContext.Default.InstrumentDataDeleteResponse),
                };
            });

            return routes;
        }
    }
}
