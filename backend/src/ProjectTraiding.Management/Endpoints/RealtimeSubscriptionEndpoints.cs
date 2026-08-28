using Npgsql;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.StorageBase.Postgres;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class RealtimeSubscriptionEndpointsLog;

    public static class RealtimeSubscriptionEndpoints
    {
        /// <summary>Операция подписки: выбор метода писателя делается обычным ветвлением.</summary>
        private enum SubscriptionOperation
        {
            EnableTrades,
            DisableTrades,
            EnableOrderbook,
            DisableOrderbook,
            EnableCandles,
            DisableCandles,
            EnableInstrument,
            DisableInstrument
        }

        public static IEndpointRouteBuilder MapRealtimeSubscriptionEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/management/realtime-subscriptions/{secid}/trades/enable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.EnableTrades,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/trades/enable",
                    "enable_trades",
                    "trades",
                    enabled: true,
                    ct));

            routes.MapGet("/management/realtime-subscriptions/{secid}/trades/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.DisableTrades,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/trades/disable",
                    "disable_trades",
                    "trades",
                    enabled: false,
                    ct));

            routes.MapGet("/management/realtime-subscriptions/{secid}/orderbook/enable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.EnableOrderbook,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/orderbook/enable",
                    "enable_orderbook",
                    "orderbook",
                    enabled: true,
                    ct));

            routes.MapGet("/management/realtime-subscriptions/{secid}/orderbook/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.DisableOrderbook,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/orderbook/disable",
                    "disable_orderbook",
                    "orderbook",
                    enabled: false,
                    ct));

            routes.MapGet("/management/realtime-subscriptions/{secid}/candles/enable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.EnableCandles,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/candles/enable",
                    "enable_candles",
                    "candles",
                    enabled: true,
                    ct));

            routes.MapGet("/management/realtime-subscriptions/{secid}/candles/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.DisableCandles,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/candles/disable",
                    "disable_candles",
                    "candles",
                    enabled: false,
                    ct));

            routes.MapGet("/management/realtime-subscriptions/{secid}/disable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.DisableInstrument,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/disable",
                    "disable_instrument",
                    "all",
                    enabled: false,
                    ct));

            routes.MapGet("/management/realtime-subscriptions/{secid}/enable", async (
                string secid,
                RealtimeSubscriptionWriter writer,
                ILogger<RealtimeSubscriptionEndpointsLog> logger,
                CancellationToken ct) =>
                await HandleAsync(
                    SubscriptionOperation.EnableInstrument,
                    secid,
                    writer,
                    logger,
                    "GET /management/realtime-subscriptions/{secid}/enable",
                    "enable_instrument",
                    "all",
                    enabled: true,
                    ct));

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

        private static async Task<IResult> HandleAsync(
            SubscriptionOperation operation,
            string secid,
            RealtimeSubscriptionWriter writer,
            ILogger logger,
            string route,
            string operationName,
            string dataKind,
            bool enabled,
            CancellationToken ct)
        {
            ManagementEndpointLogMessages.OperationStarted(logger, route);

            try
            {
                ManagementWriteResult w = operation switch
                {
                    SubscriptionOperation.EnableTrades => await writer.EnableTradesAsync(secid, ct),
                    SubscriptionOperation.DisableTrades => await writer.DisableTradesAsync(secid, ct),
                    SubscriptionOperation.EnableOrderbook => await writer.EnableOrderbookAsync(secid, ct),
                    SubscriptionOperation.DisableOrderbook => await writer.DisableOrderbookAsync(secid, ct),
                    SubscriptionOperation.EnableCandles => await writer.EnableCandlesAsync(secid, ct),
                    SubscriptionOperation.DisableCandles => await writer.DisableCandlesAsync(secid, ct),
                    SubscriptionOperation.EnableInstrument => await writer.EnableInstrumentAsync(secid, ct),
                    SubscriptionOperation.DisableInstrument => await writer.DisableInstrumentAsync(secid, ct),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };

                if (enabled && w.RowsWritten == 0)
                {
                    ManagementEndpointLogMessages.WriteBlockedByDeletion(logger, route, secid);
                    return Results.Text(
                        BlockedText(secid),
                        "text/plain",
                        statusCode: StatusCodes.Status409Conflict);
                }

                RealtimeSubscriptionResultDto dto = new(
                    operationName, secid, dataKind, enabled, w.RowsWritten, w.Elapsed.TotalMilliseconds);
                return Results.Json(dto, ManagementJsonContext.Default.RealtimeSubscriptionResultDto);
            }
            catch (PostgresException ex)
            {
                string? message = ManagementDbErrors.MapSubscription(logger, route, ex);
                if (message is null)
                    throw;

                return Results.BadRequest(message);
            }
        }

        private static string BlockedText(string secid)
        {
            return "по инструменту выполняется удаление данных, включение приёма невозможно";
        }
    }
}
