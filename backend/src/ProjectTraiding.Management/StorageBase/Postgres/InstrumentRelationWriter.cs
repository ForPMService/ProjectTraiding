using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Management.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public sealed class InstrumentRelationWriter
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<InstrumentRelationWriter> _logger;

        public InstrumentRelationWriter(NpgsqlDataSource dataSource, ILogger<InstrumentRelationWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<ManagementWriteResult> UpsertAsync(InstrumentRelationCreateRequest request, CancellationToken ct)
        {
            const string table = "moex_instrument_relations";
            ManagementWriterLogMessages.WriteStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            try
            {
                // ON CONFLICT по ключу UNIQUE NULLS NOT DISTINCT → перезапись редактируемых полей.
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_instrument_relations
                    (source_secid, target_secid, target_asset_code, relation_type, confidence, comment)
                VALUES
                    (@source_secid, @target_secid, @target_asset_code, @relation_type, @confidence, @comment)
                ON CONFLICT (source_secid, target_secid, target_asset_code, relation_type) DO UPDATE SET
                    confidence = EXCLUDED.confidence,
                    comment    = EXCLUDED.comment
                RETURNING id
                """, connection, transaction);

                // Все колонки text. Валидатор уже гарантировал непустые source/relation/confidence (!).
                // nullable target/comment → DBNull.Value. Дат здесь нет (урок 42804 — в тарифах).
                cmd.Parameters.Add("@source_secid", NpgsqlDbType.Text).Value = request.SourceSecid!;
                cmd.Parameters.Add("@target_secid", NpgsqlDbType.Text).Value = (object?)request.TargetSecid ?? DBNull.Value;
                cmd.Parameters.Add("@target_asset_code", NpgsqlDbType.Text).Value = (object?)request.TargetAssetCode ?? DBNull.Value;
                cmd.Parameters.Add("@relation_type", NpgsqlDbType.Text).Value = request.RelationType!;
                cmd.Parameters.Add("@confidence", NpgsqlDbType.Text).Value = request.Confidence!;
                cmd.Parameters.Add("@comment", NpgsqlDbType.Text).Value = (object?)request.Comment ?? DBNull.Value;

                object? scalar = await cmd.ExecuteScalarAsync(ct);
                long id = (long)scalar!;   // RETURNING id: bigint → long

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.WriteCompleted(_logger, table, id, 1, elapsed);

                return new ManagementWriteResult(Id: id, RowsWritten: 1, Elapsed: elapsed);
            }
            catch (Exception ex)
            {
                // Любой rollback = Error (consistency с Moex). FK-опечатка оператора (23503) тоже сюда —
                // известная шумность Error-уровня, endpoint переведёт её в 400-текст.
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, table, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
