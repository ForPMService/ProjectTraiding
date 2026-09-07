using Npgsql;
using ProjectTraiding.CustomFeatures.Contracts;
using ProjectTraiding.CustomFeatures.StorageBase.Postgres;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Moex.Contracts;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class ManualCalendarEndpointsLog;
    public static class ManualCalendarEndpoints
    {
        public static IEndpointRouteBuilder MapManualCalendarEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/calendar/periods", async (
                TradingPeriodBatchCreateRequest request,
                TradingPeriodWriter writer,
                ILogger<ManualCalendarEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/calendar/periods";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                string? error = ValidateTradingPeriodBatch(request);
                if (error is not null)
                {
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                try
                {
                    List<TradingPeriodCreateCommand> commands = new(request.Periods.Count);
                    for (int index = 0; index < request.Periods.Count; index++)
                        commands.Add(CreateTradingPeriodCommand(request.Periods[index]));
                    IReadOnlyList<Guid> ids = await writer.CreateManyAsync(commands, ct);
                    return Results.Json(
                        new TradingPeriodBatchCreateResponse(ids),
                        ManagementJsonContext.Default.TradingPeriodBatchCreateResponse);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapTradingPeriod(logger, route, ex);

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapPut("/management/calendar/periods/{id}", async (
                Guid id,
                TradingPeriodCreateRequest request,
                TradingPeriodWriter writer,
                ILogger<ManualCalendarEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "PUT /management/calendar/periods/{id}";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                string? error = ValidateTradingPeriod(request);
                if (error is not null)
                {
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }

                try
                {
                    int rowsWritten = await writer.UpdateAsync(
                        id, CreateTradingPeriodCommand(request), ct);
                    if (rowsWritten == 0)
                        return Results.NotFound("Строка не найдена либо является автоматической.");
                    return CalendarResponse(rowsWritten);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapTradingPeriod(logger, route, ex);
                    if (message is null)
                        throw;
                    return Results.BadRequest(message);
                }
            });

            routes.MapDelete("/management/calendar/periods/{id}", async (
                Guid id,
                TradingPeriodWriter writer,
                ILogger<ManualCalendarEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "DELETE /management/calendar/periods/{id}";
                ManagementEndpointLogMessages.OperationStarted(logger, route);
                int rowsWritten = await writer.DeleteAsync(id, ct);
                if (rowsWritten == 0)
                    return Results.NotFound("Строка не найдена либо является автоматической.");
                return CalendarResponse(rowsWritten);
            });

            return routes;
        }

        private static IResult CalendarResponse(int rowsWritten)
        {
            return Results.Json(
                new CalendarOperationResponse(rowsWritten),
                ManagementJsonContext.Default.CalendarOperationResponse);
        }

        private static string? ValidateTradingPeriod(TradingPeriodCreateRequest request)
        {
            if (request.TradeDate is null)
                return "tradeDate обязателен";
            if (!MoexDomainRules.IsMarket(request.Market))
                return "market должен быть stock или futures";
            if (string.IsNullOrWhiteSpace(request.PeriodType))
                return "periodType обязателен";
            if (request.TimeFrom is null)
                return "timeFrom обязателен";
            return null;
        }

        private static string? ValidateTradingPeriodBatch(TradingPeriodBatchCreateRequest request)
        {
            if (request.Periods is null || request.Periods.Count == 0)
                return "periods обязателен и не может быть пустым";

            for (int index = 0; index < request.Periods.Count; index++)
            {
                TradingPeriodCreateRequest? period = request.Periods[index];
                if (period is null)
                    return $"periods[{index}] обязателен";

                string? error = ValidateTradingPeriod(period);
                if (error is not null)
                    return $"periods[{index}]: {error}";
            }

            return null;
        }

        private static TradingPeriodCreateCommand CreateTradingPeriodCommand(
            TradingPeriodCreateRequest request)
        {
            return new TradingPeriodCreateCommand(
                request.TradeDate!.Value, request.Market!,
                request.Boardid ?? string.Empty, request.Secid ?? string.Empty,
                request.PeriodType!, request.TimeFrom!.Value,
                request.Session, request.TimeTill, request.Note);
        }

    }
}
