using ProjectTraiding.Moex.Realtime;

namespace ProjectTraiding.Moex.Endpoints;

/// <summary>
/// Отладочная точка пробника потокового соединения. Временная: ручная разведка контракта
/// источника перед проектированием приёмника. Не будущий API.
/// </summary>
public static class WebSocketProbeEndpoints
{
    public static IEndpointRouteBuilder MapWebSocketProbeEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/debug/realtime/ws");

        group.MapGet("/probe", async (
            string destination,
            string selector,
            int? seconds,
            MoexWebSocketProbeClient client,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                return Results.BadRequest("destination обязателен");
            }

            if (string.IsNullOrWhiteSpace(selector))
            {
                return Results.BadRequest("selector обязателен");
            }

            int effectiveSeconds = seconds ?? 15;
            if (effectiveSeconds is < 1 or > 60)
            {
                return Results.BadRequest("seconds должен быть в диапазоне от 1 до 60");
            }

            WebSocketProbeReport report = await client.ProbeAsync(
                destination, selector, TimeSpan.FromSeconds(effectiveSeconds), ct);

            return Results.Ok(report);
        });

        return routes;
    }
}
