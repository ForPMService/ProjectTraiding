using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record InstrumentRelationCreateRequest
    {
        public string? SourceSecid { get; init; }
        public string? TargetSecid { get; init; }
        public string? TargetAssetCode { get; init; }
        public string? RelationType { get; init; }   // future_underlying | same_underlying | manual_related
        public string? Confidence { get; init; }   // auto | manual
        public string? Comment { get; init; }
    }
}
