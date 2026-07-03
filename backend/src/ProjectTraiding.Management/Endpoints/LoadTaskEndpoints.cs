using Npgsql;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.Expansion;
using ProjectTraiding.Management.StorageBase.Postgres;
using ProjectTraiding.Management.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Endpoints
{
    internal sealed class LoadTaskEndpointsLog;
    internal readonly record struct LoadTaskBulkValidationKey(
        string Secid,
        string Market,
        string Boardid,
        string DataKind,
        int? CandleInterval);

    public static class LoadTaskEndpoints
    {
        public static IEndpointRouteBuilder MapLoadTaskEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/load-tasks", async (
                LoadTaskCreateRequest request,
                LoadTaskWriter writer,
                ILogger<LoadTaskEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                ValidationResult validation = LoadTaskValidator.Validate(request);
                if (!validation.IsValid)
                {
                    string errors = string.Join("; ", validation.Errors);
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
                    return Results.BadRequest(errors);
                }

                try
                {
                    Guid taskId = await writer.CreateAsync(request, ct);
                    // Идентификатор задачи — uuid; общий ManagementResultDto.Id рассчитан на long,
                    // поэтому отдаём taskId отдельно текстом, а не втискиваем в общий DTO.
                    return Results.Text($"load task created: taskId={taskId}", "text/plain");
                }
                catch (PostgresException ex)
                {
                    ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
                    string? message = ex.SqlState switch
                    {
                        "23503" => "secid не найден среди инструментов (FK)",
                        "23514" => "недопустимое значение market или storage_target (страховка)",
                        _ => null
                    };

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            routes.MapPost("/management/load-tasks/bulk", async (
                LoadTaskBulkCreateRequest request,
                LoadTaskWriter writer,
                ILogger<LoadTaskEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks/bulk";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                ValidationResult shapeValidation = ValidateBulkRequestShape(request);
                if (!shapeValidation.IsValid)
                {
                    string errors = string.Join("; ", shapeValidation.Errors);
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
                    return Results.BadRequest(errors);
                }

                ValidationResult rangeValidation = ValidateBulkInstrumentRanges(request);
                if (!rangeValidation.IsValid)
                {
                    string errors = string.Join("; ", rangeValidation.Errors);
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
                    return Results.BadRequest(errors);
                }

                int? normalizedSliceWeeks = request.SliceWeeks.HasValue
                   ? (request.SliceWeeks.Value < 1 ? 1 : request.SliceWeeks.Value)
                   : (int?)null;
                IReadOnlyList<LoadTaskCreateRequest> tasks = LoadTaskBulkExpander.Expand(request);

                ValidationResult validation = ValidateBulkExpandedTasks(tasks);
                if (!validation.IsValid)
                {
                    string errors = string.Join("; ", validation.Errors);
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
                    return Results.BadRequest(errors);
                }

                try
                {
                    BulkCreateResult result = await writer.CreateManyAsync(tasks, ct);
                    ManagementEndpointLogMessages.BulkLoadTasksCreated(
                        logger,
                        route,
                        result.ExpandedCount,
                        result.InsertedCount,
                        result.SkippedDuplicateCount);

                    LoadTaskBulkCreateResponse response = new(
                        ExpandedCount: result.ExpandedCount,
                        InsertedCount: result.InsertedCount,
                        SkippedDuplicateCount: result.SkippedDuplicateCount,
                        SliceWeeks: normalizedSliceWeeks);

                    return Results.Json(
                        response,
                        ManagementJsonContext.Default.LoadTaskBulkCreateResponse);
                }
                catch (PostgresException ex)
                {
                    ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
                    string? message = ex.SqlState switch
                    {
                        "23503" => "secid не найден среди инструментов (FK)",
                        "23514" => "недопустимое значение market или storage_target (страховка)",
                        _ => null
                    };

                    if (message is null)
                        throw;

                    return Results.BadRequest(message);
                }
            });

            return routes;
        }

        private static ValidationResult ValidateBulkRequestShape(LoadTaskBulkCreateRequest? request)
        {
            ValidationResult result = new();
            if (request is null)
            {
                result.Errors.Add("body обязателен");
                return result;
            }

            if (request.Instruments is null || request.Instruments.Count == 0)
                result.Errors.Add("instruments обязателен и не может быть пустым");

            if (request.StockDataKinds is null)
                result.Errors.Add("stock_data_kinds обязателен");

            if (request.FuturesDataKinds is null)
                result.Errors.Add("futures_data_kinds обязателен");

            if (request.CandleIntervals is null)
                result.Errors.Add("candle_intervals обязателен");

            if (string.IsNullOrWhiteSpace(request.StorageTarget))
                result.Errors.Add("storage_target обязателен");

            return result;
        }

        private static ValidationResult ValidateBulkInstrumentRanges(LoadTaskBulkCreateRequest request)
        {
            ValidationResult result = new();

            for (int i = 0; i < request.Instruments.Count; i++)
            {
                LoadTaskBulkInstrumentRequest instrument = request.Instruments[i];
                if (instrument is null)
                {
                    result.Errors.Add($"instruments[{i}] обязателен");
                    continue;
                }

                if (instrument.DateFrom > instrument.DateTill)
                    result.Errors.Add($"instruments[{i}]: date_from не может быть позже date_till");
            }

            return result;
        }

        private static ValidationResult ValidateBulkExpandedTasks(IReadOnlyList<LoadTaskCreateRequest> tasks)
        {
            ValidationResult result = new();
            HashSet<LoadTaskBulkValidationKey> seen = new();
            List<LoadTaskCreateRequest> representatives = new();

            for (int i = 0; i < tasks.Count; i++)
            {
                LoadTaskCreateRequest task = tasks[i];
                LoadTaskBulkValidationKey key = new(
                    Secid: task.Secid,
                    Market: task.Market,
                    Boardid: task.Boardid,
                    DataKind: task.DataKind,
                    CandleInterval: task.CandleInterval);

                if (seen.Add(key))
                    representatives.Add(task);
            }

            for (int i = 0; i < representatives.Count; i++)
            {
                LoadTaskCreateRequest representative = representatives[i];
                ValidationResult validation = LoadTaskValidator.Validate(representative);
                if (validation.IsValid)
                    continue;

                for (int errorIndex = 0; errorIndex < validation.Errors.Count; errorIndex++)
                {
                    result.Errors.Add(
                        $"secid={representative.Secid}, market={representative.Market}, boardid={representative.Boardid}, " +
                        $"data_kind={representative.DataKind}, candle_interval={representative.CandleInterval}: " +
                        validation.Errors[errorIndex]);
                }
            }

            return result;
        }
    }
}
