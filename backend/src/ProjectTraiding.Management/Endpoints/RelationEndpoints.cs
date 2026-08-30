using Npgsql;
using ProjectTraiding.CustomFeatures.Contracts;
using ProjectTraiding.CustomFeatures.StorageBase.Postgres;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Endpoints
{
    // Тип-маркер категории логгера: статический класс эндпоинтов нельзя как T в ILogger<T>.
    internal sealed class RelationEndpointsLog;
    public static class RelationEndpoints
    {
        public static IEndpointRouteBuilder MapRelationEndpoints(this IEndpointRouteBuilder routes)
        {
            routes.MapPost("/management/relations", async (
                InstrumentRelationCreateRequest request,   // биндинг тела — нужен ManagementJsonContext в чейне (шаг 4)
                InstrumentRelationWriter writer,
                ILogger<RelationEndpointsLog> logger,
                CancellationToken ct) =>
            {
                const string route = "POST /management/relations";
                ManagementEndpointLogMessages.OperationStarted(logger, route);

                // 1. Валидация входа.
                ValidationResult validation = InstrumentRelationValidator.Validate(request);
                if (!validation.IsValid)
                {
                    string errors = string.Join("; ", validation.Errors);
                    ManagementEndpointLogMessages.ValidationRejected(logger, route, errors);
                    return Results.BadRequest(errors);
                }

                // 2. Запись. PostgresException приходит из writer'а (rollback там уже залогирован).
                try
                {
                    InstrumentRelationUpsertCommand command = new(
                        request.SourceSecid!, request.RelationType!, request.Confidence!,
                        request.TargetSecid, request.TargetAssetCode, request.Comment);
                    ContextWriteResult w = await writer.UpsertAsync(command, ct);
                    ManagementResultDto dto = new(
                        Operation: "upsert_instrument_relation",
                        Target: "moex_instrument_relations",
                        Status: "ok",
                        Id: w.Id.Value,
                        RowsWritten: w.RowsWritten,
                        ElapsedMs: w.Elapsed.TotalMilliseconds);
                    return Results.Json(dto, ManagementJsonContext.Default.ManagementResultDto);
                }
                catch (PostgresException ex)
                {
                    // Маппинг кодов БД в текст. Известные коды → 400, неизвестный → пробросить (500).
                    string? message = ManagementDbErrors.MapRelation(
                        logger, route, ex, request.SourceSecid, request.TargetSecid);

                    if (message is null)
                        throw;   // неизвестный код БД → пусть всплывёт как 500

                    return Results.BadRequest(message);
                }
            });

            return routes;
        }
    }
}
