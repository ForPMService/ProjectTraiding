using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Endpoints
{
    internal sealed class CalendarEndpointsLog;
    public static class CalendarEndpoints
    {
        public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/vitrine/calendar", async (
                CalendarReadQuery query,
                ILogger<CalendarEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /vitrine/calendar";
                VitrineEndpointLogMessages.OperationStarted(logger, route);
                List<VitrineCalendarDayDto> items = await query.GetAllAsync(ct);
                return Results.Json(items, VitrineJsonContext.Default.ListVitrineCalendarDayDto);
            });

            return routes;
        }
    }
}
