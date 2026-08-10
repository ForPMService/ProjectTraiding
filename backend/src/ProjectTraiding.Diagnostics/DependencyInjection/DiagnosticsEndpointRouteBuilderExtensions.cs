using ProjectTraiding.Diagnostics.Endpoints;

namespace ProjectTraiding.Diagnostics.DependencyInjection;

public static class DiagnosticsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Все диагностические точки вызова висят под единым префиксом. Префикс задан группой,
    /// а не строками внутри наборов: защита отладочного слоя при публичном выставлении
    /// строится по префиксу пути, и маршрут, объявленный от корня, прошёл бы мимо неё.
    /// Групповое объявление делает такую ошибку невозможной по построению.
    /// </summary>
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder diagnostics = routes.MapGroup("/diagnostics");

        diagnostics.MapAlgopackEndpoints();
        diagnostics.MapRealtimeDebugEndpoints();
        diagnostics.MapDiagnosticDebugEndpoints();

        return routes;
    }
}
