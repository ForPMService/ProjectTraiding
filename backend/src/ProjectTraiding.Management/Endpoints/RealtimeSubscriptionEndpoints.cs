using Npgsql;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.StorageBase.Postgres;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class RealtimeSubscriptionEndpointsLog;

    public static class RealtimeSubscriptionEndpoints
    {
        public static IEndpointRouteBuilder MapRealtimeSubscriptionEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/management/realtime-subscriptions/{secid}/trades/enable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/realtime-subscriptions/{secid}/trades/enable";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                try
                {
                    ManagementWriteResult w = await writer.EnableTradesAsync(secid, ct);
                    if (w.RowsWritten == 0)
                    {
                        ManagementEndpointLogMessages.WriteBlockedByDeletion(logger, route, secid);
                        return Results.Text(
                            "по инструменту выполняется удаление данных, включение приёма невозможно",
                            "text/plain", statusCode: StatusCodes.Status409Conflict);
                    }

                    RealtimeSubscriptionResultDto dto = new(
                        "enable_trades", secid, "trades", true, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapSubscription(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapGet("/management/realtime-subscriptions/{secid}/trades/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/realtime-subscriptions/{secid}/trades/disable";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                try
                {
                    ManagementWriteResult w = await writer.DisableTradesAsync(secid, ct);
                    RealtimeSubscriptionResultDto dto = new(
                        "disable_trades", secid, "trades", false, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapSubscription(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapGet("/management/realtime-subscriptions/{secid}/orderbook/enable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/realtime-subscriptions/{secid}/orderbook/enable";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                try
                {
                    ManagementWriteResult w = await writer.EnableOrderbookAsync(secid, ct);
                    if (w.RowsWritten == 0)
                    {
                        ManagementEndpointLogMessages.WriteBlockedByDeletion(logger, route, secid);
                        return Results.Text(
                            "по инструменту выполняется удаление данных, включение приёма невозможно",
                            "text/plain", statusCode: StatusCodes.Status409Conflict);
                    }

                    RealtimeSubscriptionResultDto dto = new(
                        "enable_orderbook", secid, "orderbook", true, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapSubscription(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapGet("/management/realtime-subscriptions/{secid}/orderbook/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/realtime-subscriptions/{secid}/orderbook/disable";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                try
                {
                    ManagementWriteResult w = await writer.DisableOrderbookAsync(secid, ct);
                    RealtimeSubscriptionResultDto dto = new(
                        "disable_orderbook", secid, "orderbook", false, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapSubscription(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapGet("/management/realtime-subscriptions/{secid}/candles/enable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/realtime-subscriptions/{secid}/candles/enable";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                try
                {
                    ManagementWriteResult w = await writer.EnableCandlesAsync(secid, ct);
                    if (w.RowsWritten == 0)
                    {
                        ManagementEndpointLogMessages.WriteBlockedByDeletion(logger, route, secid);
                        return Results.Text(
                            "по инструменту выполняется удаление данных, включение приёма невозможно",
                            "text/plain", statusCode: StatusCodes.Status409Conflict);
                    }

                    RealtimeSubscriptionResultDto dto = new(
                        "enable_candles", secid, "candles", true, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapSubscription(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapGet("/management/realtime-subscriptions/{secid}/candles/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/realtime-subscriptions/{secid}/candles/disable";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                try
                {
                    ManagementWriteResult w = await writer.DisableCandlesAsync(secid, ct);
                    RealtimeSubscriptionResultDto dto = new(
                        "disable_candles", secid, "candles", false, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapSubscription(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapGet("/management/realtime-subscriptions/{secid}/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "GET /management/realtime-subscriptions/{secid}/disable";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                try
                {
                    ManagementWriteResult w = await writer.DisableInstrumentAsync(secid, ct);
                    RealtimeSubscriptionResultDto dto = new(
                        "disable_instrument", secid, "all", false, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapSubscription(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapPost("/management/realtime-subscriptions/disable-all", async (
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/realtime-subscriptions/disable-all";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                ManagementWriteResult w = await writer.DisableAllAsync(ct);
                RealtimeSubscriptionsDisableAllResponse dto = new(
                    "disable_all", w.RowsWritten, w.Elapsed.TotalMilliseconds);
                return Results.Json(
                    dto,
                    ManagementJsonContext.Default.RealtimeSubscriptionsDisableAllResponse);
            });

            return routes;
        }
    }
}
