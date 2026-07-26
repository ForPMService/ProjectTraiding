using ProjectTraiding.Moex.Endpoints;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection;

public static class MoexEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMoexSyncEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapInstrumentCardLoadEndpoints();
        routes.MapCalendarLoadEndpoints();
        return routes;
    }

    // Остался только календарь: по отдельному решению владельца он не переносится
    // в отладочный проект. Остальные диагностические точки вызова живут
    // в диагностическом проекте.
    public static IEndpointRouteBuilder MapMoexDiagnosticEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapCalendarEndpoints();
        return routes;
    }

   
}
