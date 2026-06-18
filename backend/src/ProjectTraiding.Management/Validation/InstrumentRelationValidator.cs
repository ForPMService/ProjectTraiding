using ProjectTraiding.Management.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Validation
{
    public static class InstrumentRelationValidator
    {
        // Словари дублируют CHECK / контракт БД(V013), но дают оператору внятный текст до SQL.
        private static readonly string[] RelationTypes =
        { "future_underlying", "same_underlying", "manual_related" };
        private static readonly string[] Confidences = { "auto", "manual" };

        public static ValidationResult Validate(InstrumentRelationCreateRequest request)
        {
            ValidationResult result = new();

            // 1. presence: source_secid (в БД NOT NULL + жёсткий FK).
            if (string.IsNullOrWhiteSpace(request.SourceSecid))
                result.Errors.Add("source_secid обязателен");

            // 2. словарь relation_type (Array.IndexOf, не LINQ).
            if (request.RelationType is null || Array.IndexOf(RelationTypes, request.RelationType) < 0)
                result.Errors.Add($"relation_type должен быть одним из: {string.Join(", ", RelationTypes)}");

            // 3. словарь confidence (в БД CHECK).
            if (request.Confidence is null || Array.IndexOf(Confidences, request.Confidence) < 0)
                result.Errors.Add($"confidence должен быть одним из: {string.Join(", ", Confidences)}");

            // 4. инвариант БД CHECK: заполнен хотя бы один target.
            if (string.IsNullOrWhiteSpace(request.TargetSecid)
                && string.IsNullOrWhiteSpace(request.TargetAssetCode))
                result.Errors.Add("нужен хотя бы один из target_secid / target_asset_code");

            return result;
        }
    }
}
