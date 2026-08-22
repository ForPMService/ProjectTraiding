using Npgsql;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.StorageBase.Postgres;
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
                    ManagementWriteResult w = await writer.UpsertAsync(request, ct);
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
                    ManagementEndpointLogMessages.DbErrorMapped(logger, route, ex.SqlState ?? "?");
                    string? message = ex.SqlState switch
                    {
                        "23503" => $"инструмент не найден (source_secid={request.SourceSecid}, target_secid={request.TargetSecid})",
                        "23514" => "нарушен инвариант: нужен хотя бы один из target_secid / target_asset_code",
                        "23505" => "связь уже существует (не должно возникать при ON CONFLICT DO UPDATE)",
                        _ => null
                    };

                    if (message is null)
                        throw;   // неизвестный код БД → пусть всплывёт как 500

                    return Results.BadRequest(message);
                }
            });

            return routes;
        }
    }
}
