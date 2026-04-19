using Microsoft.AspNetCore.Routing;
using ProjectTraiding.Api.Health;

namespace ProjectTraiding.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

        app.MapGet("/health/ready", (ConfigurationHealthChecker checker) =>
        {
            var status = checker.Check();
            return Results.Ok(status);
        });

        return app;
    }
}
