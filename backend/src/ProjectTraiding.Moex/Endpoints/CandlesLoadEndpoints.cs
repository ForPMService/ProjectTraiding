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
            routes.MapPost("/operations/moex/load/candles/{taskId:guid}", async (
                Guid taskId,
                CandlesLoadRunner runner,
                CancellationToken ct) =>
            {
                CandlesLoadOutcome outcome = await runner.RunAsync(taskId, ct);

                return outcome.Status switch
                {
                    CandlesLoadStatus.NotFound => Results.NotFound(),
                    CandlesLoadStatus.NotClaimed => Results.StatusCode(StatusCodes.Status409Conflict),
                    _ => Results.Text(
                        $"candles load done: task={taskId}, rows={outcome.RowsCovered.ToString(CultureInfo.InvariantCulture)}",
                        "text/plain"),
                };
            });

            return routes;
        }
    }
}
