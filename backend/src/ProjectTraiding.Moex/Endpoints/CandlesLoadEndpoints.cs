using ProjectTraiding.Moex.Loading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.Endpoints
{
    public static class CandlesLoadEndpoints
    {
        public static IEndpointRouteBuilder MapCandlesLoadEndpoints(this IEndpointRouteBuilder routes)
        {
            // taskId принимаем строкой, а не {taskId:guid}: типизированный Guid в маршруте
            // тянет System.Guid в схему OpenAPI, для которой нет source-generated метаданных.
            // Строку парсим сами — заодно отдаём понятный 400 на битый идентификатор.
            routes.MapPost("/operations/moex/load/candles/{taskId}", async (
               string taskId,
               LoadRunner runner,
               CancellationToken ct) =>
            {
                if (!Guid.TryParse(taskId, out Guid id))
                    return Results.BadRequest("taskId не является корректным GUID");

                LoadOutcome outcome = await runner.RunAsync(id, ct);

                return outcome.Status switch
                {
                    LoadStatus.NotFound => Results.NotFound(),
                    LoadStatus.NotClaimed => Results.StatusCode(StatusCodes.Status409Conflict),
                    _ => Results.Text(
                        $"candles load done: task={id}, rows={outcome.RowsCovered.ToString(CultureInfo.InvariantCulture)}",
                        "text/plain"),
                };
            });

            return routes;
        }
    }
}
