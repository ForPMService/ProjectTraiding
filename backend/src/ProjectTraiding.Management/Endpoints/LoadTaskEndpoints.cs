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
                    Guid? taskId = await writer.CreateAsync(request, ct);
                    if (taskId is null)
                    {
                        ManagementEndpointLogMessages.WriteBlockedByDeletion(
                            logger, route, request.Secid);
                        return Results.Text(
                            "по инструменту выполняется удаление данных, постановка задания невозможна",
                            "text/plain", statusCode: StatusCodes.Status409Conflict);
                    }

                    // Идентификатор задачи — uuid; общий ManagementResultDto.Id рассчитан на long,
                    // поэтому отдаём taskId отдельно текстом, а не втискиваем в общий DTO.
                    return Results.Text($"load task created: taskId={taskId.Value}", "text/plain");
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
                LoadedRangeCoverageReader coverageReader,
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

                // Зрелая дата — по московскому торговому дню, а не по часовому поясу процесса:
                // приложение западнее Москвы в первый час московских суток отрезало бы лишний
                // день молча, без единой ошибки в журнале.
                //
                // Смещение продублировано намеренно. В контуре Moex тот же расчёт живёт в
                // MoexTime, но ссылка Management → Moex запрещена: контуры связаны только
                // данными. Дублирование трёх часов дешевле сцепления контуров (правило 13).
                // Вернут перевод часов — править в обоих местах.
                //
                // Вычисляется один раз до цикла: весь пакет задач одной постановки оперирует
                // одной зрелой датой, даже если исполнение цикла пересечёт московскую полночь.
                DateOnly lastMatureDate =
                    DateOnly.FromDateTime(DateTime.UtcNow + TimeSpan.FromHours(3)).AddDays(-1);

                List<LoadTaskCreateRequest> tasks = new();
                for (int instrumentIndex = 0; instrumentIndex < request.Instruments.Count; instrumentIndex++)
                {
                    LoadTaskBulkInstrumentRequest instrument = request.Instruments[instrumentIndex];
                    string[] dataKinds = instrument.Market == "stock"
                        ? request.StockDataKinds
                        : request.FuturesDataKinds;

                    DateOnly effectiveFrom = instrument.DateFrom;
                    DateOnly effectiveTill = instrument.DateTill < lastMatureDate
                        ? instrument.DateTill
                        : lastMatureDate;

                    for (int intervalIndex = 0; intervalIndex < request.CandleIntervals.Length; intervalIndex++)
                    {
                        await AddMissingWindowsAsync(
                            tasks,
                            coverageReader,
                            logger,
                            route,
                            instrument,
                            dataKind: "candles",
                            candleInterval: request.CandleIntervals[intervalIndex],
                            storageTarget: request.StorageTarget,
                            requestedSliceWeeks: request.SliceWeeks,
                            effectiveFrom,
                            effectiveTill,
                            ct);
                    }

                    for (int dataKindIndex = 0; dataKindIndex < dataKinds.Length; dataKindIndex++)
                    {
                        await AddMissingWindowsAsync(
                            tasks,
                            coverageReader,
                            logger,
                            route,
                            instrument,
                            dataKind: dataKinds[dataKindIndex],
                            candleInterval: null,
                            storageTarget: request.StorageTarget,
                            requestedSliceWeeks: request.SliceWeeks,
                            effectiveFrom,
                            effectiveTill,
                            ct);
                    }
                }

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
                    if (result.BlockedSecids.Count > 0)
                    {
                        string blocked = string.Join(", ", result.BlockedSecids);
                        ManagementEndpointLogMessages.WriteBlockedByDeletion(logger, route, blocked);
                        return Results.Text(
                            "по инструментам выполняется удаление данных, ни одно задание пакета не создано: " + blocked,
                            "text/plain", statusCode: StatusCodes.Status409Conflict);
                    }

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

            routes.MapPost("/management/load-tasks/cancel", async (
                LoadTaskWriter writer,
                ILogger<LoadTaskEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/load-tasks/cancel";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                CancelResult result = await writer.CancelAllAsync(ct);
                LoadTasksCancelResponse response = new(
                    Scope: "all",
                    Secid: null,
                    CancelledCount: result.CancelledCount,
                    ElapsedMs: result.Elapsed.TotalMilliseconds);

                return Results.Json(
                    response,
                    ManagementJsonContext.Default.LoadTasksCancelResponse);
            });

            // Сегмент instruments обязателен. Сегодня конфликта нет — маршрут ручного
            // запуска живёт под другим префиксом (/operations/moex/load-tasks/{taskId}/run).
            // Но если позже понадобится адресная отмена по идентификатору задания,
            // без этого сегмента два шаблона совпали бы буквально.
            routes.MapPost("/management/load-tasks/instruments/{secid}/cancel", async (
                string secid,
                LoadTaskWriter writer,
                ILogger<LoadTaskEndpointsLog> logger,
                CancellationToken ct) =>
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
                LoadTasksCancelResponse response = new(
                    Scope: "instrument",
                    Secid: secid,
                    CancelledCount: result.CancelledCount,
                    ElapsedMs: result.Elapsed.TotalMilliseconds);

                return Results.Json(
                    response,
                    ManagementJsonContext.Default.LoadTasksCancelResponse);
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

        private static async Task AddMissingWindowsAsync(
            List<LoadTaskCreateRequest> tasks,
            LoadedRangeCoverageReader coverageReader,
            ILogger logger,
            string route,
            LoadTaskBulkInstrumentRequest instrument,
            string dataKind,
            int? candleInterval,
            string storageTarget,
            int? requestedSliceWeeks,
            DateOnly effectiveFrom,
            DateOnly effectiveTill,
            CancellationToken ct)
        {
            if (effectiveFrom > effectiveTill)
            {
                ManagementEndpointLogMessages.BulkFillMissingResolved(
                    logger,
                    route,
                    instrument.Secid,
                    dataKind,
                    candleInterval,
                    instrument.DateFrom,
                    instrument.DateTill,
                    effectiveFrom,
                    effectiveTill,
                    coveredIntervalsCount: 0,
                    missingIntervalsCount: 0,
                    missingDaysTotal: 0,
                    windowsCreatedCount: 0);
                return;
            }

            IReadOnlyList<CoverageInterval> covered = await coverageReader.GetCoveredRangesAsync(
                instrument.Secid,
                instrument.Market,
                instrument.Boardid,
                dataKind,
                candleInterval,
                storageTarget,
                effectiveFrom,
                effectiveTill,
                ct);

            CoverageSubtractResult result = LoadedRangeCoverageCalculator.Subtract(
                effectiveFrom,
                effectiveTill,
                covered);

            int windowsCreatedCount = LoadTaskBulkExpander.AddMissingWindows(
                tasks,
                instrument,
                dataKind,
                candleInterval,
                storageTarget,
                requestedSliceWeeks,
                result.Missing);

            ManagementEndpointLogMessages.BulkFillMissingResolved(
                logger,
                route,
                instrument.Secid,
                dataKind,
                candleInterval,
                instrument.DateFrom,
                instrument.DateTill,
                effectiveFrom,
                effectiveTill,
                result.CoveredIntervalsCount,
                result.MissingIntervalsCount,
                result.MissingDaysTotal,
                windowsCreatedCount);
        }
    }
}
