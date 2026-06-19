using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    public sealed record VitrineInstrumentRelationDto(
        long Id,
        string SourceSecid,
        string? TargetSecid,
        string? TargetAssetCode,
        string RelationType,           // future_underlying, same_underlying, manual_related
        string Confidence,             // auto | manual
        string? Comment);
}
