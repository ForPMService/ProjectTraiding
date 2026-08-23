using Npgsql;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Management.Validation;
using ProjectTraiding.Moex.Contracts;
using ProjectTraiding.Moex.Loading.Planning;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class LoadTaskEndpointsLog;
    internal readonly record struct LoadTaskBulkValidationKey(
        string Secid, string Market, string Boardid, string DataKind, int? CandleInterval);

    public static class LoadTaskEndpoints
    {
        public static IEndpointRouteBuilder MapLoadTaskEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/load-tasks", async (
                LoadTaskCreateRequest request,
                LoadTaskWriter writer,
                MoexLoadPlanner planner,
                ILogger<LoadTaskEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks";
                ManagementEndpointLogMessages.OperationStarted(logger, route);
                ValidationResult validation = LoadTaskValidator.Validate(request);
                if (!validation.IsValid)
                    return ValidationFailure(logger, route, validation);

                MoexLoadWindow? planned = await planner.PlanSingleAsync(new MoexLoadWindow(
                    request.Secid, request.Market, request.Boardid, request.DataKind,
                    request.CandleInterval, request.DateFrom, request.DateTill, request.StorageTarget), ct);
                if (planned is null)
                {
                    string message = $"открытый интерес публикуется по серии срочных контрактов; " +
                        $"у инструмента {request.Secid} код серии не заполнен";
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, message);
                    return Results.BadRequest(message);
                }

                try
                {
                    Guid? taskId = await writer.CreateAsync(ToRequest(planned.Value), ct);
                    if (taskId is null)
                    {
                        ManagementEndpointLogMessages.WriteBlockedByDeletion(logger, route, request.Secid);
                        return Results.Text(
                            "по инструменту выполняется удаление данных, постановка задания невозможна",
                            "text/plain", statusCode: StatusCodes.Status409Conflict);
                    }
                    return Results.Text($"load task created: taskId={taskId.Value}", "text/plain");
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapLoadTask(logger, route, ex);
                    if (message is null)
                        throw;
                    return Results.BadRequest(message);
                }
            });

            routes.MapPost("/management/load-tasks/bulk", async (
                LoadTaskBulkCreateRequest request,
                LoadTaskWriter writer,
                MoexLoadPlanner planner,
                ILogger<LoadTaskEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks/bulk";
                ManagementEndpointLogMessages.OperationStarted(logger, route);
                ValidationResult shapeValidation = ValidateBulkRequestShape(request);
                if (!shapeValidation.IsValid)
                    return ValidationFailure(logger, route, shapeValidation);
                ValidationResult rangeValidation = ValidateBulkInstrumentRanges(request);
                if (!rangeValidation.IsValid)
                    return ValidationFailure(logger, route, rangeValidation);

                List<MoexLoadPlanInstrument> instruments = new();
                for (int index = 0; index < request.Instruments.Count; index++)
                {
                    LoadTaskBulkInstrumentRequest instrument = request.Instruments[index];
                    instruments.Add(new MoexLoadPlanInstrument(
                        instrument.Secid, instrument.Market, instrument.Boardid,
                        instrument.DateFrom, instrument.DateTill));
                }

                MoexLoadPlanResult plan = await planner.PlanBulkAsync(new MoexLoadPlanRequest(
                    instruments, request.StockDataKinds, request.FuturesDataKinds,
                    request.CandleIntervals, request.StorageTarget, request.SliceWeeks), ct);
                List<LoadTaskCreateRequest> tasks = new();
                for (int index = 0; index < plan.Windows.Count; index++)
                    tasks.Add(ToRequest(plan.Windows[index]));

                ValidationResult validation = ValidateBulkExpandedTasks(tasks);
                if (!validation.IsValid)
                    return ValidationFailure(logger, route, validation);

                try
                {
                    BulkCreateResult result = await writer.CreateManyAsync(tasks, ct);
                    if (result.BlockedSecids.Count > 0)
                    {
                        string blocked = string.Join(", ", result.BlockedSecids);
                        ManagementEndpointLogMessages.WriteBlockedByDeletion(logger, route, blocked);
                        return Results.Text(
                            "по инструментам выполняется удаление данных, ни одно задание пакета не создано: " + blocked,
                            "text/plain", statusCode: StatusCodes.Status409Conflict);
                    }

                    ManagementEndpointLogMessages.BulkLoadTasksCreated(
                        logger, route, result.ExpandedCount, result.InsertedCount, result.SkippedDuplicateCount);
                    return Results.Json(new LoadTaskBulkCreateResponse(
                        result.ExpandedCount, result.InsertedCount, result.SkippedDuplicateCount,
                        plan.EffectiveSliceWeeks), ManagementJsonContext.Default.LoadTaskBulkCreateResponse);
                }
                catch (PostgresException ex)
                {
                    string? message = ManagementDbErrors.MapLoadTask(logger, route, ex);
                    if (message is null)
                        throw;
                    return Results.BadRequest(message);
                }
            });

            routes.MapPost("/management/load-tasks/cancel", async (
                LoadTaskWriter writer, ILogger<LoadTaskEndpointsLog> logger, CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks/cancel";
                ManagementEndpointLogMessages.OperationStarted(logger, route);
                CancelResult result = await writer.CancelAllAsync(ct);
                return Results.Json(new LoadTasksCancelResponse(
                    "all", null, result.CancelledCount, result.CancelRequestedCount,
                    result.Elapsed.TotalMilliseconds), ManagementJsonContext.Default.LoadTasksCancelResponse);
            });

            routes.MapPost("/management/load-tasks/instruments/{secid}/cancel", async (
                string secid, LoadTaskWriter writer, ILogger<LoadTaskEndpointsLog> logger, CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks/instruments/{secid}/cancel";
                ManagementEndpointLogMessages.OperationStarted(logger, route);
                if (string.IsNullOrWhiteSpace(secid))
                {
                    const string error = "secid обязателен";
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, error);
                    return Results.BadRequest(error);
                }
                CancelResult result = await writer.CancelInstrumentAsync(secid, ct);
                return Results.Json(new LoadTasksCancelResponse(
                    "instrument", secid, result.CancelledCount, result.CancelRequestedCount,
                    result.Elapsed.TotalMilliseconds), ManagementJsonContext.Default.LoadTasksCancelResponse);
            });

            return routes;
        }

        private static IResult ValidationFailure(ILogger logger, string route, ValidationResult validation)
        {
            string errors = string.Join("; ", validation.Errors);
            ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
            return Results.BadRequest(errors);
        }

        private static LoadTaskCreateRequest ToRequest(MoexLoadWindow window) => new(
            window.Secid, window.Market, window.Boardid, window.DataKind, window.CandleInterval,
            window.DateFrom, window.DateTill, window.StorageTarget);

        private static ValidationResult ValidateBulkRequestShape(LoadTaskBulkCreateRequest request)
        {
            ValidationResult result = new();
            if (request.Instruments is null || request.Instruments.Count == 0)
                result.Errors.Add("instruments обязателен и не может быть пустым");
            if (request.StockDataKinds is null)
                result.Errors.Add("stock_data_kinds обязателен");
            else if (!AllDataKindsAllowed(request.StockDataKinds))
                result.Errors.Add("stock_data_kinds содержит недопустимый data_kind");
            if (request.FuturesDataKinds is null)
                result.Errors.Add("futures_data_kinds обязателен");
            else if (!AllDataKindsAllowed(request.FuturesDataKinds))
                result.Errors.Add("futures_data_kinds содержит недопустимый data_kind");
            if (request.CandleIntervals is null)
                result.Errors.Add("candle_intervals обязателен");
            else if (!AllCandleIntervalsAllowed(request.CandleIntervals))
                result.Errors.Add("candle_intervals содержит недопустимый интервал");
            if (string.IsNullOrWhiteSpace(request.StorageTarget))
                result.Errors.Add("storage_target обязателен");
            else if (!MoexDomainRules.IsStorageTarget(request.StorageTarget))
                result.Errors.Add("storage_target должен быть одним из: clickhouse");
            return result;
        }

        private static bool AllDataKindsAllowed(string[] dataKinds)
        {
            for (int index = 0; index < dataKinds.Length; index++)
            {
                if (!MoexDomainRules.IsDataKind(dataKinds[index]))
                    return false;
            }
            return true;
        }

        private static bool AllCandleIntervalsAllowed(int[] intervals)
        {
            for (int index = 0; index < intervals.Length; index++)
            {
                if (!MoexDomainRules.IsCandleInterval(intervals[index]))
                    return false;
            }
            return true;
        }

        private static ValidationResult ValidateBulkInstrumentRanges(LoadTaskBulkCreateRequest request)
        {
            ValidationResult result = new();
            for (int index = 0; index < request.Instruments.Count; index++)
            {
                LoadTaskBulkInstrumentRequest instrument = request.Instruments[index];
                if (instrument is null)
                {
                    result.Errors.Add($"instruments[{index}] обязателен");
                    continue;
                }
                if (instrument.DateFrom > instrument.DateTill)
                    result.Errors.Add($"instruments[{index}]: date_from не может быть позже date_till");
            }
            return result;
        }

        private static ValidationResult ValidateBulkExpandedTasks(IReadOnlyList<LoadTaskCreateRequest> tasks)
        {
            ValidationResult result = new();
            HashSet<LoadTaskBulkValidationKey> seen = new();
            for (int index = 0; index < tasks.Count; index++)
            {
                LoadTaskCreateRequest task = tasks[index];
                if (!seen.Add(new LoadTaskBulkValidationKey(
                        task.Secid, task.Market, task.Boardid, task.DataKind, task.CandleInterval)))
                    continue;
                ValidationResult validation = LoadTaskValidator.Validate(task);
                for (int errorIndex = 0; errorIndex < validation.Errors.Count; errorIndex++)
                {
                    result.Errors.Add($"secid={task.Secid}, market={task.Market}, boardid={task.Boardid}, " +
                        $"data_kind={task.DataKind}, candle_interval={task.CandleInterval}: " +
                        validation.Errors[errorIndex]);
                }
            }
            return result;
        }
    }
}
